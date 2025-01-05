#version 150

uniform float decay;

out vec4 color;

void main() {
    color = vec4(0.0, 0.0, 0.0, decay);
}
