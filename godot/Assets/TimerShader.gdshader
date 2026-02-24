shader_type canvas_item;

uniform float value : hint_range(0.0, 1.0) = 0.5;
uniform float thickness : hint_range(0.0, 0.5) = 0.1;
uniform vec4 color : source_color = vec4(1.0, 0.75, 0.18, 1.0);
uniform vec4 bg_color : source_color = vec4(1.0, 1.0, 1.0, 0.1);

void fragment() {
    vec2 center = vec2(0.5, 0.5);
    float dist = distance(UV, center);
    
    // Create ring
    float inner_radius = 0.5 - thickness;
    float outer_radius = 0.5;
    
    if (dist > outer_radius || dist < inner_radius) {
        discard;
    }
    
    // Calculate angle (starts at top, clockwise)
    float angle = atan(UV.x - 0.5, 0.5 - UV.y);
    if (angle < 0.0) {
        angle += 2.0 * 3.14159;
    }
    
    float normalized_angle = angle / (2.0 * 3.14159);
    
    if (normalized_angle <= value) {
        COLOR = color;
    } else {
        COLOR = bg_color;
    }
}
