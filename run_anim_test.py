import time
import subprocess

def run_test():
    print("Starting server...")
    subprocess.Popen(["/Applications/Godot_mono.app/Contents/MacOS/Godot", "--headless", "--path", "godot", "--", "--server", "--test-marathon-host"], stdout=open("logs/server_auto.log", "w"), stderr=subprocess.STDOUT)
    time.sleep(2)
    
    print("Starting client 1 (Host)...")
    subprocess.Popen(["/Applications/Godot_mono.app/Contents/MacOS/Godot", "--path", "godot", "--", "--test-marathon-host"], stdout=open("logs/client1_auto.log", "w"), stderr=subprocess.STDOUT)
    time.sleep(2)

    print("Starting client 2 (Joiner)...")
    subprocess.Popen(["/Applications/Godot_mono.app/Contents/MacOS/Godot", "--path", "godot", "--", "--test-marathon-join"], stdout=open("logs/client2_auto.log", "w"), stderr=subprocess.STDOUT)
    time.sleep(10) # Let the marathon mode start the game and simulate turns

    print("Test complete. Killing instances...")
    subprocess.run(["pkill", "-9", "-f", "Godot"])

if __name__ == "__main__":
    run_test()
