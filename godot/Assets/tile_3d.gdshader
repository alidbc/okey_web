shader_type canvas_item;

uniform float bevel_size : hint_range(0.0, 0.2) = 0.05;
uniform float depth_intensity : hint_range(0.0, 1.0) = 0.3;
uniform vec4 side_color : source_color = vec4(0.8, 0.8, 0.75, 1.0);

void fragment() {
    vec4 tex_color = texture(TEXTURE, UV);
    
    // Simulate depth by darkening the bottom and right edges
    float dist_to_edge_right = 1.0 - UV.x;
    float dist_to_edge_bottom = 1.0 - UV.y;
    float dist_to_edge_left = UV.x;
    float dist_to_edge_top = UV.y;
    
    float min_dist_border = min(min(dist_to_edge_left, dist_to_edge_right), min(dist_to_edge_top, dist_to_edge_bottom));
    
    vec3 out_color = tex_color.rgb;
    
    if (min_dist_border < bevel_size) {
        // Simple bevel calculation
        if (dist_to_edge_bottom < bevel_size || dist_to_edge_right < bevel_size) {
            out_color = mix(out_color, side_color.rgb * 0.7, depth_intensity);
        } else if (dist_to_edge_left < bevel_size || dist_to_edge_top < bevel_size) {
            out_color = mix(out_color, vec3(1.0), 0.1); // subtle highlight
        }
    }
    
    COLOR = vec4(out_color, tex_color.a);
}
