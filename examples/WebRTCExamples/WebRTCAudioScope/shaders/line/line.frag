#version 150

uniform bool colorize;
uniform float base_hue;
uniform float decay;
uniform float desaturation;

in float relative_length;
in vec2 angle;
in float position;

out vec4 color;

// https://github.com/hughsk/glsl-hsv2rgb
vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void main() {
    float alpha = mix(1.0 - decay, 1.0, position);
    if (colorize) {
        float phase = log2(angle.x);
        float saturation = exp2(-desaturation * angle.y); // more noise -> less saturation
        color = vec4(hsv2rgb(vec3(base_hue + phase, saturation, 1.0)), alpha);
    } else {
        color = vec4(hsv2rgb(vec3(base_hue, 1.0, 1.0)), alpha);
    }
}
