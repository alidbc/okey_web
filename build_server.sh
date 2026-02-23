#!/bin/bash

# Configuration
GODOT_BIN="/Applications/Godot_mono.app/Contents/MacOS/Godot"
EXPORT_PRESET="Linux Headless"
EXPORT_PATH="./exports/server/OkieRummyServer.x86_64"
DOCKER_IMAGE_NAME="okierummy-server"

echo "=== Starting Okey Rummy Server Build ==="

# Create export directory
EXPORT_DIR="$(pwd)/godot/exports/server"
mkdir -p "$EXPORT_DIR"

# Export Godot project for headless Linux
echo "Exporting for $EXPORT_PRESET..."
$GODOT_BIN --headless --path ./godot --export-release "$EXPORT_PRESET" "$EXPORT_DIR/OkieRummyServer.x86_64"

if [ $? -eq 0 ]; then
    echo "Export successful: $EXPORT_DIR"
else
    echo "Export failed!"
    exit 1
fi

# Build Docker image
echo "Building Docker image: $DOCKER_IMAGE_NAME..."
docker build -t $DOCKER_IMAGE_NAME .

if [ $? -eq 0 ]; then
    echo "Docker build successful!"
    echo "To run the server locally:"
    echo "  docker run -p 8080:8080/udp $DOCKER_IMAGE_NAME"
else
    echo "Docker build failed!"
    exit 1
fi

echo "=== Build Complete ==="
