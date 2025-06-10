#!/bin/bash
# SilentRemote Server Cleanup Script
# This script stops and removes all Docker containers, networks, and volumes
# while preserving SSL/TLS certificates

echo "=== SilentRemote Server Cleanup Script ==="
echo "This script will:"
echo "1. Backup SSL/TLS certificates"
echo "2. Stop and remove all Docker containers, networks, and volumes"
echo "3. Remove unnecessary files and directories"
echo "4. Restore SSL/TLS certificates"

# Set up backup directory
BACKUP_DIR="/root/certificate_backup_$(date +%Y%m%d_%H%M%S)"
CERT_DIR="/etc/letsencrypt/live"
DOMAINS=("web-bridge.nextcloudcyber.com" "relay.nextcloudcyber.com" "monitoring.nextcloudcyber.com")

echo -e "\n=== Backing up SSL/TLS certificates ==="
mkdir -p $BACKUP_DIR
for DOMAIN in "${DOMAINS[@]}"; do
  if [ -d "$CERT_DIR/$DOMAIN" ]; then
    echo "Backing up certificates for $DOMAIN"
    mkdir -p $BACKUP_DIR/$DOMAIN
    cp -r $CERT_DIR/$DOMAIN/* $BACKUP_DIR/$DOMAIN/
    echo "Backed up certificates for $DOMAIN to $BACKUP_DIR/$DOMAIN/"
  else
    echo "Warning: Certificate directory for $DOMAIN not found!"
  fi
done

echo -e "\n=== Stopping and removing Docker containers ==="
# Stop and remove all containers
docker stop $(docker ps -a -q) 2>/dev/null || echo "No containers to stop"
docker rm $(docker ps -a -q) 2>/dev/null || echo "No containers to remove"

# Remove all Docker images (optional - uncomment if needed)
# echo -e "\n=== Removing Docker images ==="
# docker rmi $(docker images -q) 2>/dev/null || echo "No images to remove"

echo -e "\n=== Removing Docker networks ==="
# Remove all custom networks (keeps default networks)
docker network ls | grep -v "bridge\|host\|none" | awk '{print $1}' | xargs -r docker network rm 2>/dev/null || echo "No networks to remove"

echo -e "\n=== Removing Docker volumes ==="
# Remove all Docker volumes
docker volume rm $(docker volume ls -q) 2>/dev/null || echo "No volumes to remove"

echo -e "\n=== Cleaning up application directories ==="
# Clean up application directories (adjust paths as needed)
if [ -d "/opt/quasar-relay" ]; then
  # Save the certificate directory if it exists
  if [ -d "/opt/quasar-relay/certs" ]; then
    mkdir -p $BACKUP_DIR/quasar-certs
    cp -r /opt/quasar-relay/certs/* $BACKUP_DIR/quasar-certs/
    echo "Backed up /opt/quasar-relay/certs/ to $BACKUP_DIR/quasar-certs/"
  fi
  
  # Remove application directory
  rm -rf /opt/quasar-relay
  mkdir -p /opt/quasar-relay/certs
  echo "Cleaned up /opt/quasar-relay directory"
fi

# Clean up web bridge directory if it exists
if [ -d "/root/uasar-web-bridge" ]; then
  # Save the certificate directory if it exists
  if [ -d "/root/uasar-web-bridge/certs" ]; then
    mkdir -p $BACKUP_DIR/web-bridge-certs
    cp -r /root/uasar-web-bridge/certs/* $BACKUP_DIR/web-bridge-certs/
    echo "Backed up /root/uasar-web-bridge/certs/ to $BACKUP_DIR/web-bridge-certs/"
  fi
  
  # Remove application directory
  rm -rf /root/uasar-web-bridge
  mkdir -p /root/uasar-web-bridge/certs
  echo "Cleaned up /root/uasar-web-bridge directory"
fi

echo -e "\n=== Restoring SSL/TLS certificates ==="
# Restore certificates to the web bridge directory
if [ -d "$BACKUP_DIR/web-bridge-certs" ]; then
  mkdir -p /root/uasar-web-bridge/certs
  cp -r $BACKUP_DIR/web-bridge-certs/* /root/uasar-web-bridge/certs/
  chmod 644 /root/uasar-web-bridge/certs/*.pem
  echo "Restored certificates to /root/uasar-web-bridge/certs/"
fi

# Restore certificates to quasar-relay directory
if [ -d "$BACKUP_DIR/quasar-certs" ]; then
  mkdir -p /opt/quasar-relay/certs
  cp -r $BACKUP_DIR/quasar-certs/* /opt/quasar-relay/certs/
  chmod 644 /opt/quasar-relay/certs/*.pem
  echo "Restored certificates to /opt/quasar-relay/certs/"
fi

echo -e "\n=== Cleanup complete ==="
echo "Certificate backups are stored in: $BACKUP_DIR"
echo "Server has been cleaned up and is ready for fresh deployment."
echo "Remember to copy your Let's Encrypt certificates back to your application directories if needed."
