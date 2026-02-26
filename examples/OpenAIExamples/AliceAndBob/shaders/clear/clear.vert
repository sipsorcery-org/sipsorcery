#version 150

in vec2 vec;

void main() {
    gl_Position = vec4(vec, 1.0, 1.0);
}
