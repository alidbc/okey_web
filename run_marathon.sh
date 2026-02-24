#!/bin/bash

# Kill any existing Godot instances
pkill -9 -f Godot || true
sleep 2

mkdir -p logs

echo "Starting Server..."
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --server --test-mode > logs/server.log 2>&1 &
SERVER_PID=$!
sleep 2

echo "Starting Host (Client 1)..."
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --test-marathon-host --no-server > logs/host.log 2>&1 &
HOST_PID=$!
sleep 2

echo "Starting Joiner 1 (Client 2)..."
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --test-marathon-join --no-server > logs/join1.log 2>&1 &
JOIN1_PID=$!
sleep 1

echo "Starting Joiner 2 (Client 3)..."
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --test-marathon-join --no-server > logs/join2.log 2>&1 &
JOIN2_PID=$!
sleep 1

echo "Starting Joiner 3 (Client 4)..."
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --test-marathon-join --no-server > logs/join3.log 2>&1 &
JOIN3_PID=$!

echo "All instances started."
echo "PIDs: Server=$SERVER_PID, Host=$HOST_PID, J1=$JOIN1_PID, J2=$JOIN2_PID, J3=$JOIN3_PID"
