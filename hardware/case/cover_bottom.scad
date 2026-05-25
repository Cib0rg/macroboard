// ==========================================
// НИЖНЯЯ КРЫШКА (дно, бобышки, бортик, вырез под разъём питания)
// ==========================================

brim_correction = 2;

pcb_w = 173.0;
pcb_h = 125.0 + 4;
margin = 2.0;
case_w = pcb_w + margin*2;
case_h = pcb_h + margin*2;

// Позиции крепёжных отверстий
hole_LU = [5, 7.0];
hole_LD = [5, pcb_h - 7.3 - brim_correction + 1.5];
hole_RU = [pcb_w - 5.2, 7.0];
hole_RD = [pcb_w - 5.2 - 1, pcb_h - 7.3];

holes_left  = [hole_LU, hole_LD];
holes_right = [hole_RU, hole_RD];
all_holes = concat(holes_left, holes_right);

// Основные размеры
plate_thick = 2.0;
boss_dia = 6.0;

// Параметры втулок
insert_outer_dia = 4.2;
insert_length = 5;        // 3/4/5/6 мм
floor_reserve = 1.2;

boss_height = insert_length + floor_reserve;   // 6.2 мм

// Стенки (бортик)
wall_thick = 2.0;
wall_height = boss_height + 1.7;   // 7.9 мм

// Вырез под разъём питания (верхняя стенка)
cutout_x_start = 18;
cutout_x_end = 32;
cutout_depth = 6;  // высота выреза от верхнего края стенки

$fn = 64;

module bottom_cover() {
    difference() {
        union() {
            // Сплошное дно
            cube([case_w, case_h, plate_thick]);
            
            // Бобышки
            for (pos = all_holes) {
                x = margin + pos[0];
                y = margin + pos[1];
                translate([x, y, plate_thick])
                    cylinder(d = boss_dia, h = boss_height);
            }
            
            // Стенки по периметру (бортик)
            translate([0, 0, plate_thick]) {
                difference() {
                    cube([case_w, case_h, wall_height]);
                    translate([wall_thick, wall_thick, -0.01])
                        cube([case_w - 2*wall_thick, case_h - 2*wall_thick, wall_height + 0.02]);
                }
            }
        }
        
        // Глухие отверстия под втулки
        for (pos = all_holes) {
            x = margin + pos[0];
            y = margin + pos[1];
            translate([x, y, plate_thick + boss_height - insert_length])
                cylinder(d = insert_outer_dia, h = insert_length);
        }
        
        // Вырез в верхней стенке (Y = case_h) для разъёма питания
        translate([
            cutout_x_start,
            case_h - wall_thick,                       // внутренняя грань стенки
            plate_thick + wall_height - cutout_depth   // верх минус глубина выреза
        ])
        cube([
            cutout_x_end - cutout_x_start,             // ширина
            wall_thick + 0.01,                         // сквозь всю толщину стенки
            cutout_depth + 0.01                        // высота с небольшим перекрытием
        ]);
    }
}

bottom_cover();