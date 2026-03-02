shader_type canvas_item;

uniform float speed : hint_range(0.0, 5.0) = 1.0;
uniform float width : hint_range(0.0, 1.0) = 0.2;
uniform float intensity : hint_range(0.0, 1.0) = 0.3;
uniform float scale : hint_range(0.1, 10.0) = 1.0;

void fragment() {
    float time = TIME * speed;
    float position = mod(time, 3.0) - 1.0; 
    
    float sweep = smoothstep(position, position + width, UV.x) - smoothstep(position + width, position + width * 2.0, UV.x);
    
    // Output white with alpha controlled by the sweep
    COLOR = vec4(1.0, 1.0, 1.0, sweep * intensity);
}
