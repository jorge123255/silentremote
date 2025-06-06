#!/bin/bash

# Build custom client with specified relay URL
# Usage: ./build-client.sh <client-name> <platform> [relay-url] [server-id]

# Default values
DEFAULT_RELAY="wss://relay.nextcloudcyber.com"
DEFAULT_SERVER_ID="silentremote-server-1"

# Get parameters
CLIENT_NAME=$1
PLATFORM=$2
RELAY_URL=${3:-$DEFAULT_RELAY}
SERVER_ID=${4:-$DEFAULT_SERVER_ID}

# Validate inputs
if [ -z "$CLIENT_NAME" ] || [ -z "$PLATFORM" ]; then
    echo "Usage: ./build-client.sh <client-name> <platform> [relay-url] [server-id]"
    echo ""
    echo "Platforms: win-x64, osx-x64, linux-x64"
    echo "Example: ./build-client.sh remote-client1 win-x64"
    exit 1
fi

# Validate platform
if [[ "$PLATFORM" != "win-x64" && "$PLATFORM" != "osx-x64" && "$PLATFORM" != "linux-x64" ]]; then
    echo "Invalid platform: $PLATFORM"
    echo "Supported platforms: win-x64, osx-x64, linux-x64"
    exit 1
fi

# Launch the server application with the build command
echo "Building client: $CLIENT_NAME for platform $PLATFORM"
echo "Using relay URL: $RELAY_URL"
echo "Using server ID: $SERVER_ID"
echo ""

# Make sure server application is executable
chmod +x ./SilentRemote.Server

# Create a temporary config file with the custom relay URL
cat > ./temp_serverconfig.json << EOF
{
  "ServerId": "$SERVER_ID",
  "RelayUrl": "$RELAY_URL",
  "AuthToken": "test-token",
  "ClientBuildPath": "../../../SilentRemote.Client/SilentRemote.Client.csproj",
  "ClientOutputPath": "clients"
}
EOF

# Move the temp config file to the main config
mv ./temp_serverconfig.json ./serverconfig.json

# Start the server and send the build command
echo "Starting server and building client..."
echo "build $CLIENT_NAME $PLATFORM" | ./SilentRemote.Server

echo ""
echo "Client build process complete."
echo "Look for the client package in the 'clients' directory."
