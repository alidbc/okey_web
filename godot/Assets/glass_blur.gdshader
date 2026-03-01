shader_type canvas_item;

uniform float lod: hint_range(0.0, 5.0) = 2.0;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;

void fragment() {
    COLOR = textureLod(screen_texture, SCREEN_UV, lod);
    // Add a dark tint for better contrast
    COLOR.rgb *= 0.5;
    COLOR.a = 1.0;
}
