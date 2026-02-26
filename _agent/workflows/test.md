---
description: Start server and 1-4 game clients (default 2)
---

// turbo
1. Start the Okey Rummy server and multiple client instances
```bash
# Kill existing processes
pkill Godot ; 

# Build the project
dotnet build godot/OkieRummyGodot.csproj && \

# Run headless server
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path godot -- --server & \
sleep 2; 

# Determine number of clients (1-4, defaults to 2 if empty or invalid)
INPUT_VAL="${1:-2}"
case "$INPUT_VAL" in
  1|2|3|4) NUM_CLIENTS=$INPUT_VAL ;;
  *) NUM_CLIENTS=2 ;;
esac

echo "Launching $NUM_CLIENTS client instances..."

for i in $(seq 1 $NUM_CLIENTS); do
  open -n /Applications/Godot_mono.app --args --path /Users/ali/Downloads/okey_rummy-master/godot --profile=client_$i
done
```
