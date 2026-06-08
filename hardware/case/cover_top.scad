// ==========================================
// ЕДИНАЯ КРЫШКА (левая + правая части) + стенки + вырез
// ==========================================

brim_correction = 2;

pcb_w = 173.0;
pcb_h = 125.0 + 4;
margin = 2.0;
case_w = pcb_w + margin*2;
case_h = pcb_h + margin*2;
lid_thick = 2.0;

// Параметры зон
left_space_height = 14.0;    // левая часть выше
right_space_height = 12.6;   // правая часть ниже

split_x_from_left = 42.6;    // граница от левого края платы
split_x_abs = margin + split_x_from_left; // абсолютная X границы

// Размеры частей (крышки с перекрытием для соединения)
left_part_width = split_x_abs+1;
right_part_width = case_w - split_x_abs+2;
right_part_x_offset = split_x_abs-2;

// Размеры для стенок (строго до границы, без перекрытия)
left_wall_width = split_x_abs;
right_wall_width = case_w - split_x_abs;

// Отверстия для винтов
hole_LU = [5, 7.0];
hole_LD = [5, pcb_h - 7.3 - brim_correction + 1.5];
hole_RU = [pcb_w - 5.2, 7.0];
hole_RD = [pcb_w - 5.2 - 1, pcb_h - 7.3];
holes_left = [hole_LU, hole_LD];
holes_right = [hole_RU, hole_RD];

screw_dia = 3.2;
countersink_dia = 6.0;
countersink_depth = 0.8;

// Бобышки (увеличен внутренний диаметр)
boss_dia = 6.0;
boss_hole_dia = 3.4;

// Энкодер
encoder_w = 17.5;
encoder_h = 14.2;
encoder_from_left = 18.0;
encoder_from_bottom = 29.0 + 1;
encoder_y_top = pcb_h - encoder_from_bottom;

// Кнопки
button_size = 19.1;
button_hole_tolerance = 0.3;
final_hole = button_size + button_hole_tolerance;
row1_top_offset = 38.0;
row2_bottom_offset = 12.6;
button_x_offsets = [46.0, 70.0, 94.0, 117.0, 142.0];

// Экраны (круглые)
display_x_offsets = [45, 69, 93, 116, 141];

// Стенки
wall_thick = 2.0;

// Вырез в верхнем левом углу (в стенке)
cutout_x_start = 11;
cutout_x_end = 42;

// Дополнительный вырез в верхней плоскости (крышке) под USB
usb_cutout_length = 20;  // длина по оси Y (глубина)

$fn = 64;

// ===== МОДУЛИ ЛЕВОЙ ЧАСТИ =====

module left_lid() {
    translate([0, 0, left_space_height])
        cube([left_part_width, case_h, lid_thick]);
}

module left_bosses() {
    for (pos = holes_left) {
        x = margin + pos[0];
        y = margin + pos[1];
        if (x < split_x_abs) {
            translate([x, y, 0])
                difference() {
                    cylinder(d = boss_dia, h = left_space_height);
                    translate([0, 0, -0.1])
                        cylinder(d = boss_hole_dia, h = left_space_height + 0.2);
                }
        }
    }
}

module left_screw_holes() {
    for (pos = holes_left) {
        x = margin + pos[0];
        y = margin + pos[1];
        if (x < split_x_abs) {
            translate([x, y, left_space_height - 0.1]) {
                cylinder(d = screw_dia, h = lid_thick + 0.2);
                translate([0, 0, lid_thick - countersink_depth])
                    cylinder(d1 = screw_dia, d2 = countersink_dia, h = countersink_depth + 0.1);
            }
        }
    }
}

// Изменение 1: диаметр отверстия под энкодер увеличен до 15 мм
module left_encoder_cutout() {
    x = margin + encoder_from_left;
    y = margin + encoder_y_top;
    center_x = x + encoder_w / 2;
    center_y = y + encoder_h / 2;
    r = 15 / 2;   // было 7/2, изменено на 15/2
    translate([center_x, center_y, left_space_height - 0.1])
        cylinder(h = lid_thick + 0.2, r = r);
}

module left_walls() {
    difference() {
        // Внешний габарит
        cube([left_wall_width, case_h, left_space_height]);
        // Внутренний вырез: убирает всё, кроме левой, передней и задней стенок
        translate([wall_thick, wall_thick, -0.01])
            cube([left_wall_width - wall_thick, case_h - 2*wall_thick, left_space_height + 0.02]);
        // Вырез в передней стенке под разъём питания (Y = 0)
        translate([cutout_x_start, -0.01, -0.01])
            cube([cutout_x_end - cutout_x_start, wall_thick + 0.02, left_space_height + 0.02]);
    }
}

// Изменение 2: вырез в верхней плоскости левой крышки (длина 20 мм по Y)
module left_lid_usb_cutout() {
    width = cutout_x_end - cutout_x_start;  // 31 мм
    translate([cutout_x_start, 0, left_space_height])
        cube([width, usb_cutout_length, lid_thick + 0.1]);
}

// ===== МОДУЛИ ПРАВОЙ ЧАСТИ =====

module right_lid() {
    translate([right_part_x_offset, 0, right_space_height])
        cube([right_part_width, case_h, lid_thick]);
}

module right_bosses() {
    for (pos = holes_right) {
        x = margin + pos[0];
        y = margin + pos[1];
        if (x >= split_x_abs) {
            translate([x, y, 0])
                difference() {
                    cylinder(d = boss_dia, h = right_space_height);
                    translate([0, 0, -0.1])
                        cylinder(d = boss_hole_dia, h = right_space_height + 0.2);
                }
        }
    }
}

module right_screw_holes() {
    for (pos = holes_right) {
        x = margin + pos[0];
        y = margin + pos[1];
        if (x >= split_x_abs) {
            translate([x, y, right_space_height - 0.1]) {
                cylinder(d = screw_dia, h = lid_thick + 0.2);
                translate([0, 0, lid_thick - countersink_depth])
                    cylinder(d1 = screw_dia, d2 = countersink_dia, h = countersink_depth + 0.1);
            }
        }
    }
}

module right_button_cutouts() {
    for (x_off = button_x_offsets) {
        x = margin + x_off;
        if (x >= split_x_abs) {
            y1 = margin + row1_top_offset;
            y2 = margin + (pcb_h - row2_bottom_offset - final_hole);
            for (y = [y1, y2]) {
                translate([x, y, right_space_height - 0.1])
                    linear_extrude(height = lid_thick + 0.2)
                        square([final_hole, final_hole]);
            }
        }
    }
}

module right_display_cutouts() {
    for (x_off = display_x_offsets) {
        x = margin + x_off + 10.5;
        if (x >= split_x_abs) {
            y_upper = margin + 24.5;
            translate([x, y_upper, right_space_height - 0.1])
                cylinder(d = 21, h = lid_thick + 0.2);

            y_lower = margin + pcb_h - 45;
            translate([x, y_lower, right_space_height - 0.1])
                cylinder(d = 21, h = lid_thick + 0.2);
        }
    }
}

module right_walls() {
    difference() {
        translate([split_x_abs, 0, 0])
            cube([right_wall_width, case_h, right_space_height]);
        translate([split_x_abs, wall_thick, -0.01])
            cube([right_wall_width - wall_thick, case_h - 2*wall_thick, right_space_height + 0.02]);
    }
}

mirror([0,1,0])   // исправлено: была ошибка в синтаксисе mirror(0,1,0) -> mirror([0,1,0])

// ===== СБОРКА =====
union() {
    // Левая часть
    left_bosses();
    difference() {
        left_lid();
        left_screw_holes();
        left_encoder_cutout();
        left_lid_usb_cutout();   // добавлен вырез в верхней плоскости
    }
    left_walls();

    // Правая часть
    right_bosses();
    difference() {
        right_lid();
        right_screw_holes();
        right_button_cutouts();
        right_display_cutouts();
    }
    right_walls();
}

// Визуализация платы
%translate([margin, margin, -1.6])
    cube([pcb_w, pcb_h, 1.6]);