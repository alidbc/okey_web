shader_type canvas_item;

uniform sampler2D wood_texture : source_color, repeat_enable;
uniform float tiling_factor : hint_range(0.1, 10.0) = 1.0;
uniform float cutout_width : hint_range(0.0, 1.0) = 0.18;
uniform float cutout_height : hint_range(0.0, 1.0) = 0.6;
uniform float border_width : hint_range(0.0, 0.1) = 0.02;

void fragment() {
    // Crop the rack texture to the central wood area (removing the side handles)
    // and the top half (removing the bottom ledge)
    float start_x = 0.08;
    float end_x = 0.92;
    float start_y = 0.05;
    float end_y = 0.45;
    
    // Calculate the cropped UV range
    vec2 cropped_uv = vec2(
        mix(start_x, end_x, fract(UV.x * tiling_factor)),
        mix(start_y, end_y, UV.y)
    );
    
    vec2 uv = UV;
    float dist_x = abs(uv.x - 0.5);
    
    // Tight curve using powered cosine for a steeper arch that flattens faster
    float curve = 0.0;
    if (dist_x < cutout_width) {
        float n = dist_x / cutout_width;
        curve = pow(cos(n * PI * 0.5), 4.0) * cutout_height;
    }
    
    // Discard the arched part on the bottom
    if (uv.y > (1.0 - curve)) {
        discard;
    }
    
    // Sample cropped and tiled wood texture
    vec4 tex = texture(wood_texture, cropped_uv);
    
    // Add depth border at the bottom edge of the cut
    float edge_dist = abs(uv.y - (1.0 - curve));
    if (edge_dist < border_width) {
        float shadow = smoothstep(0.0, border_width, edge_dist);
        tex.rgb *= mix(0.4, 1.0, shadow);
    }
    
    // Subtle top highlight for a polished wood look
    if (uv.y < 0.03) {
        tex.rgb += vec3(0.12);
    }
    
    COLOR = tex;
}
