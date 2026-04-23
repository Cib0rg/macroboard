idf_component_register(
    SRCS
        "main.c"
        "hardware/gc9a01.c"
        "hardware/gc9a01_backlight.c"
        "hardware/display_mux.c"
        "hardware/buttons.c"
        "hardware/encoder.c"
        "hardware/leds.c"
        "usb/usb_hid_keyboard.c"
        "usb/usb_vendor.c"
        "usb/usb_descriptors.c"
        "protocol/protocol_handler.c"
        "protocol/packet_parser.c"
        "protocol/image_transfer.c"
        "storage/nvs_manager.c"
        "storage/profile_storage.c"
        "storage/image_storage.c"
        "profile/profile_manager.c"
        "profile/action_executor.c"
        "utils/logger.c"
        "utils/crc.c"
    INCLUDE_DIRS
        "."
        "hardware"
        "usb"
        "protocol"
        "storage"
        "profile"
        "utils"
    REQUIRES
        driver
        nvs_flash
        spiffs
        esp_timer
        esp_tinyusb
    EMBED_FILES
        "assets/back_icon.jpg"
)

# Add linker flags to force inclusion of TinyUSB callbacks
target_link_libraries(${COMPONENT_LIB} INTERFACE "-u tud_hid_descriptor_report_cb")
target_link_libraries(${COMPONENT_LIB} INTERFACE "-u tud_hid_get_report_cb")
target_link_libraries(${COMPONENT_LIB} INTERFACE "-u tud_hid_set_report_cb")
target_link_libraries(${COMPONENT_LIB} INTERFACE "-u tud_vendor_rx_cb")
