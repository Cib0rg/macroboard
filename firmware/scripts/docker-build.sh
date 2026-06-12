#!/bin/bash
#
# Docker Build Script for ESP32-S3 Macro Keyboard
# 
# Этот скрипт собирает прошивку ESP32-S3 используя Docker контейнер
# с официальным образом ESP-IDF от Espressif.
#
# Использование:
#   ./scripts/docker-build.sh [options]
#
# Опции:
#   -c, --clean     Очистить сборку перед компиляцией
#   -v, --verbose   Подробный вывод
#   -h, --help      Показать справку
#
# Примеры:
#   ./scripts/docker-build.sh              # Обычная сборка
#   ./scripts/docker-build.sh --clean      # Очистить и собрать
#   ./scripts/docker-build.sh --verbose    # Подробный вывод
#

set -e  # Выход при ошибке

# Цвета для вывода
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Переменные
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIRMWARE_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT_DIR="$FIRMWARE_DIR"
DOCKER_IMAGE="espressif/idf:v5.3"
CLEAN_BUILD=false
VERBOSE=false

# Число параллельных jobs: Windows (NUMBER_OF_PROCESSORS) → nproc → 4
CPU_COUNT="${NUMBER_OF_PROCESSORS:-$(nproc 2>/dev/null || echo 4)}"

# Функции для вывода
print_info() {
    echo -e "${BLUE}ℹ️  $1${NC}"
}

print_success() {
    echo -e "${GREEN}✅ $1${NC}"
}

print_warning() {
    echo -e "${YELLOW}⚠️  $1${NC}"
}

print_error() {
    echo -e "${RED}❌ $1${NC}"
}

print_header() {
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
}

# Показать справку
show_help() {
    cat << EOF
Docker Build Script for ESP32-S3 Macro Keyboard

Использование:
    $0 [options]

Опции:
    -c, --clean     Очистить сборку перед компиляцией (fullclean)
    -v, --verbose   Подробный вывод компиляции
    -h, --help      Показать эту справку

Примеры:
    $0                  # Обычная сборка
    $0 --clean          # Очистить и собрать заново
    $0 --verbose        # Подробный вывод компиляции
    $0 -c -v            # Очистить и собрать с подробным выводом

Требования:
    - Docker (версия 20.10+)
    - Docker Compose (версия 2.0+)
    - Проект в директории: $PROJECT_DIR

Переменные окружения:
    DOCKER_IMAGE        Docker образ ESP-IDF (по умолчанию: espressif/idf:v5.3)
    IDF_TARGET          Целевая платформа (по умолчанию: esp32s3)

EOF
}

# Парсинг аргументов
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--clean)
            CLEAN_BUILD=true
            shift
            ;;
        -v|--verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            show_help
            exit 0
            ;;
        *)
            print_error "Неизвестная опция: $1"
            echo "Используйте --help для справки"
            exit 1
            ;;
    esac
done

# Проверка наличия Docker
check_docker() {
    print_info "Проверка Docker..."
    
    if ! command -v docker &> /dev/null; then
        print_error "Docker не установлен!"
        echo ""
        echo "Установите Docker:"
        echo "  sudo apt-get update"
        echo "  sudo apt-get install docker.io docker-compose"
        echo "  sudo usermod -aG docker \$USER"
        echo "  newgrp docker"
        exit 1
    fi
    
    if ! docker ps &> /dev/null; then
        print_error "Docker не запущен или нет прав доступа!"
        echo ""
        echo "Проверьте:"
        echo "  1. Docker запущен: sudo systemctl start docker"
        echo "  2. Пользователь в группе docker: sudo usermod -aG docker \$USER"
        echo "  3. Перелогиньтесь или выполните: newgrp docker"
        exit 1
    fi
    
    print_success "Docker доступен"
}

# Проверка наличия образа
check_image() {
    print_info "Проверка Docker образа $DOCKER_IMAGE..."
    
    if ! docker image inspect "$DOCKER_IMAGE" &> /dev/null; then
        print_warning "Образ $DOCKER_IMAGE не найден локально"
        print_info "Скачивание образа (это может занять несколько минут)..."
        
        if docker pull "$DOCKER_IMAGE"; then
            print_success "Образ успешно скачан"
        else
            print_error "Не удалось скачать образ!"
            exit 1
        fi
    else
        print_success "Образ найден"
    fi
}

# Проверка структуры проекта
check_project() {
    print_info "Проверка структуры проекта..."
    
    if [ ! -d "$PROJECT_DIR" ]; then
        print_error "Директория проекта не найдена: $PROJECT_DIR"
        echo ""
        echo "Создайте проект:"
        echo "  cd $FIRMWARE_DIR"
        echo "  docker run --rm -v \"\$PWD:/project\" -w /project $DOCKER_IMAGE idf.py create-project macro-keyboard"
        exit 1
    fi
    
    if [ ! -f "$PROJECT_DIR/CMakeLists.txt" ]; then
        print_error "CMakeLists.txt не найден в $PROJECT_DIR"
        echo ""
        echo "Убедитесь, что это корректный проект ESP-IDF"
        exit 1
    fi
    
    print_success "Структура проекта корректна"
}

# Очистка сборки
clean_build() {
    print_header "Очистка сборки"
    
    print_info "Выполнение fullclean..."
    
    docker run --rm \
        -v "$PROJECT_DIR:/project" \
        -w /project \
        -e "IDF_TARGET=esp32s3" \
        "$DOCKER_IMAGE" \
        idf.py fullclean
    
    print_success "Сборка очищена"
    echo ""
}

# Сборка проекта
build_project() {
    print_header "Сборка прошивки ESP32-S3"
    
    print_info "Проект: $PROJECT_DIR"
    print_info "Docker образ: $DOCKER_IMAGE"
    print_info "Целевая платформа: esp32s3"
    print_info "Параллельных jobs: $CPU_COUNT"
    echo ""

    # Подготовка команды
    BUILD_CMD="idf.py -j $CPU_COUNT build"

    if [ "$VERBOSE" = true ]; then
        BUILD_CMD="$BUILD_CMD -v"
    fi
    
    # Время начала
    START_TIME=$(date +%s)
    
    print_info "Запуск сборки..."
    echo ""
    
    # Запуск Docker контейнера для сборки
    if docker run --rm \
        --cpus "$CPU_COUNT" \
        -v "$PROJECT_DIR:/project" \
        -v "elgato-ccache:/root/.ccache" \
        -v "elgato-esp-config:/root/.espressif" \
        -w /project \
        -e "IDF_TARGET=esp32s3" \
        -e "IDF_CCACHE_ENABLE=1" \
        -e "CCACHE_DIR=/root/.ccache" \
        -e "CCACHE_MAXSIZE=2G" \
        "$DOCKER_IMAGE" \
        $BUILD_CMD; then
        
        # Время окончания
        END_TIME=$(date +%s)
        BUILD_TIME=$((END_TIME - START_TIME))
        
        echo ""
        print_success "Сборка завершена успешно!"
        print_info "Время сборки: ${BUILD_TIME} секунд"
        
        # Показать информацию о размере
        if [ -f "$PROJECT_DIR/build/macro-keyboard.bin" ]; then
            echo ""
            print_header "Информация о прошивке"
            
            # Размер файлов
            APP_SIZE=$(stat -f%z "$PROJECT_DIR/build/macro-keyboard.bin" 2>/dev/null || stat -c%s "$PROJECT_DIR/build/macro-keyboard.bin" 2>/dev/null)
            APP_SIZE_KB=$((APP_SIZE / 1024))
            
            print_info "Размер прошивки: ${APP_SIZE_KB} KB"
            
            # Показать детальную информацию о размере
            echo ""
            print_info "Детальная информация о размере:"
            docker run --rm \
                -v "$PROJECT_DIR:/project" \
                -w /project \
                "$DOCKER_IMAGE" \
                idf.py size
            
            echo ""
            print_header "Файлы прошивки"
            print_info "Расположение: $PROJECT_DIR/build/"
            echo ""
            ls -lh "$PROJECT_DIR/build/"*.bin 2>/dev/null || true
        fi
        
        echo ""
        print_success "Готово! Прошивка находится в: $PROJECT_DIR/build/"
        echo ""
        print_info "Следующие шаги:"
        echo "  - Прошить устройство: ./scripts/docker-flash.sh"
        echo "  - Или использовать: make flash PORT=/dev/ttyUSB0"
        
        return 0
    else
        echo ""
        print_error "Сборка завершилась с ошибкой!"
        echo ""
        print_info "Попробуйте:"
        echo "  1. Очистить сборку: $0 --clean"
        echo "  2. Проверить логи с подробным выводом: $0 --verbose"
        echo "  3. Проверить конфигурацию: make menuconfig"
        
        return 1
    fi
}

# Показать статистику ccache
show_ccache_stats() {
    print_header "Статистика ccache"
    
    docker run --rm \
        -v "elgato-ccache:/root/.ccache" \
        "$DOCKER_IMAGE" \
        ccache -s
    
    echo ""
}

# Главная функция
main() {
    print_header "ESP32-S3 Macro Keyboard - Docker Build"
    echo ""
    
    # Проверки
    check_docker
    check_image
    check_project
    
    echo ""
    
    # Очистка если нужно
    if [ "$CLEAN_BUILD" = true ]; then
        clean_build
    fi
    
    # Сборка
    if build_project; then
        echo ""
        show_ccache_stats
        exit 0
    else
        exit 1
    fi
}

# Запуск
main
