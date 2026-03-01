shader_type canvas_item;

uniform float grain_amount : hint_range(0.0, 1.0) = 0.15;
uniform float grain_size : hint_range(0.1, 5.0) = 1.0;
uniform float vignette_intensity : hint_range(0.0, 1.0) = 0.45;
uniform float vignette_opacity : hint_range(0.0, 1.0) = 0.6;
uniform vec4 base_felt_color : source_color = vec4(0.08, 0.20, 0.13, 1.0);

// Simple pseudo-random function for noise
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

void fragment() {
    // Procedural grain based on screen UVs for a steady, paper-like feel
    vec2 grain_uv = SCREEN_UV * 2000.0 * grain_size;
    float n = hash(floor(grain_uv));
    
    // Smooth the noise slightly for a fabric feel
    float grain = mix(0.9, 1.1, n) * grain_amount;
    vec3 felt_rgb = base_felt_color.rgb * (1.0 + (n - 0.5) * grain_amount);
    
    // Vignette calculation
    vec2 centralized_uv = UV - vec2(0.5);
    float dist = length(centralized_uv);
    float vignette = smoothstep(0.5, 0.5 - vignette_intensity, dist);
    
    // Apply vignette (darken edges)
    felt_rgb = mix(felt_rgb * (1.0 - vignette_opacity), felt_rgb, vignette);
    
    COLOR = vec4(felt_rgb, 1.0);
}
