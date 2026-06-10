#!/bin/bash
# Stop MacroKeyboard Backend processes
# Usage: ./stop-backend.sh

echo "Searching for MacroKeyboard.Backend processes..."

# Find processes by name
PROCESSES=$(ps aux | grep "[M]acroKeyboard.Backend" | awk '{print $2}')

if [ -z "$PROCESSES" ]; then
    echo "No MacroKeyboard.Backend processes found"
    
    # Check if port is still in use
    echo ""
    echo "Checking port 28195..."
    
    if command -v lsof &> /dev/null; then
        PORT_INFO=$(lsof -i :28195 2>/dev/null)
        if [ -n "$PORT_INFO" ]; then
            echo "Port 28195 is in use:"
            echo "$PORT_INFO"
            
            PID=$(echo "$PORT_INFO" | tail -n 1 | awk '{print $2}')
            if [ -n "$PID" ]; then
                echo ""
                read -p "Kill process $PID? (y/n): " response
                if [ "$response" = "y" ]; then
                    kill -9 $PID
                    echo "Process $PID killed"
                fi
            fi
        else
            echo "Port 28195 is free"
        fi
    elif command -v netstat &> /dev/null; then
        PORT_INFO=$(netstat -tulpn 2>/dev/null | grep :28195)
        if [ -n "$PORT_INFO" ]; then
            echo "Port 28195 is in use:"
            echo "$PORT_INFO"
        else
            echo "Port 28195 is free"
        fi
    else
        echo "Neither lsof nor netstat available, cannot check port"
    fi
    
    exit 0
fi

echo "Found process(es):"
ps aux | grep "[M]acroKeyboard.Backend"

echo ""
read -p "Stop all MacroKeyboard.Backend processes? (y/n): " response

if [ "$response" = "y" ]; then
    for PID in $PROCESSES; do
        echo "Stopping process $PID..."
        kill $PID
    done
    
    # Wait a bit
    sleep 1
    
    # Force kill if still running
    for PID in $PROCESSES; do
        if ps -p $PID > /dev/null 2>&1; then
            echo "Force killing process $PID..."
            kill -9 $PID
        fi
    done
    
    echo "All processes stopped"
else
    echo "Operation cancelled"
fi
