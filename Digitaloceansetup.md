# Quasar Relay Server: DigitalOcean Quick Start Guide

This guide will walk you through setting up the Quasar relay server on a DigitalOcean Droplet, including all necessary security configurations.

## 1. Create a DigitalOcean Account

1. Go to [DigitalOcean](https://www.digitalocean.com/) and sign up for an account if you don't have one
2. Verify your email address and set up payment information

## 2. Create a Droplet

1. Log in to your DigitalOcean account
2. Click "Create" > "Droplets" in the top navigation
3. Choose the following settings:
   - **Distribution**: Ubuntu 22.04 LTS
   - **Plan**: Basic
   - **CPU option**: Regular (Shared CPU)
   - **Size**: 4GB RAM / 2 vCPUs ($20/month) - This is a good starting point
   - **Datacenter Region**: Choose the region closest to your users
   - **VPC Network**: Leave as default
   - **Authentication**: Choose SSH keys (recommended) or Password
     - If using SSH keys, follow DigitalOcean's guide to add your public key
   - **Hostname**: quasar-relay-production (or your preferred name)
   - **Backups**: Enable (recommended for production)
   - **Monitoring**: Enable

4. Click "Create Droplet"
5. Wait for your Droplet to be created (usually takes less than a minute)

## 3. Set Up DNS

1. Purchase a domain name if you don't have one (Namecheap, GoDaddy, etc.)
2. In DigitalOcean, go to "Networking" > "Domains"
3. Add your domain and click "Add Domain"
4. Create the following DNS records:
   - **A Record**: 
     - Hostname: @ (or your preferred subdomain like 'relay')
     - Will direct to: Your Droplet
   - **A Record**:
     - Hostname: monitoring (for the Grafana dashboard)
     - Will direct to: Your Droplet

5. If your domain is managed elsewhere, add these A records in your domain registrar's DNS settings:
   - `yourdomain.com` → [Your Droplet's IP]
   - `monitoring.yourdomain.com` → [Your Droplet's IP]

## 4. Connect to Your Droplet

```bash
ssh root@your-droplet-ip
```

Replace `your-droplet-ip` with the IP address of your Droplet.

## 5. Update Server & Install Prerequisites

```bash
# Update package listings
apt update

# Upgrade existing packages
apt upgrade -y

# Install required packages
apt install -y docker.io docker-compose certbot nginx curl git ufw

# Start Docker and enable on boot
systemctl start docker
systemctl enable docker

# Add your user to the docker group (if not using root)
# usermod -aG docker $USER
```

## 6. Set Up Firewall

```bash
# Allow SSH
ufw allow 22/tcp

# Allow HTTP and HTTPS
ufw allow 80/tcp
ufw allow 443/tcp

# Allow STUN server ports
ufw allow 3478/udp
ufw allow 3479/udp

# Enable firewall
ufw enable
```

## 7. Clone Repository and Set Up Files

```bash
# Create directory for the project
mkdir -p /opt/quasar-relay
cd /opt/quasar-relay

# Clone your repository (replace with your actual repository URL)
git clone https://github.com/your-username/quasar.git .

# Create required directories
mkdir -p config/nginx config/prometheus config/relay certs logs/relay1 logs/relay2 data/relay1 data/relay2 backups
```

## 8. Configure Nginx for SSL Setup

```bash
# Stop Nginx if it's running
systemctl stop nginx

# Get SSL certificate
certbot certonly --standalone -d yourdomain.com -d monitoring.yourdomain.com

# Copy certificates to our directory
cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem /opt/quasar-relay/certs/
cp /etc/letsencrypt/live/yourdomain.com/privkey.pem /opt/quasar-relay/certs/
chmod 644 /opt/quasar-relay/certs/*.pem
```

## 9. Update Configuration Files

You need to update the following files with your domain name and other specific settings:

1. Edit `config/nginx/relay.conf`:
   ```bash
   nano config/nginx/relay.conf
   ```
   
   Replace all instances of `your-domain.com` with your actual domain name.

2. Edit `config/relay/relay-config.json`:
   ```bash
   nano config/relay/relay-config.json
   ```
   
   Verify settings match your needs, especially rate limiting and security settings.

3. Edit `docker-compose.production.yml`:
   ```bash
   nano docker-compose.production.yml
   ```
   
   Replace all instances of `your-domain.com` with your actual domain.

## 10. Create Backup Script

```bash
mkdir -p config/backup
nano config/backup/backup.sh
```

Add the following content:

```bash
#!/bin/bash
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_DIR="/opt/quasar-relay/backups"

# Backup data directories
tar -czf "$BACKUP_DIR/data_$TIMESTAMP.tar.gz" -C /opt/quasar-relay data

# Backup logs (last 7 days)
find /opt/quasar-relay/logs -type f -mtime -7 | tar -czf "$BACKUP_DIR/logs_$TIMESTAMP.tar.gz" -T -

# Backup configs
tar -czf "$BACKUP_DIR/configs_$TIMESTAMP.tar.gz" -C /opt/quasar-relay config

# Cleanup old backups (keep last 14 days)
find "$BACKUP_DIR" -type f -name "*.tar.gz" -mtime +14 -delete

echo "Backup completed at $TIMESTAMP" >> "$BACKUP_DIR/backup.log"
```

Make it executable:
```bash
chmod +x config/backup/backup.sh
```

## 11. Create Grafana Password

Create a secure password for Grafana:

```bash
echo "GRAFANA_ADMIN_PASSWORD=your-secure-password" > .env
```

Replace `your-secure-password` with a strong password.

## 12. Start the Docker Stack

```bash
docker-compose -f docker-compose.production.yml up -d
```

## 13. Verify Everything Is Running

```bash
# Check if containers are running
docker ps

# Check logs for any errors
docker logs relay-load-balancer
docker logs quasar-relay-1
```

## 14. Access Your Services

- Relay Server: `https://yourdomain.com`
- Monitoring Dashboard: `https://monitoring.yourdomain.com`
  - Username: admin
  - Password: [The password you set in the .env file]

## 15. Testing Your Relay Server

1. Use the Quasar client to connect to your relay server
2. Check the logs to verify connections are being established
3. Monitor the Grafana dashboard for metrics

## 16. Troubleshooting

If you encounter issues:

1. Check container logs:
   ```bash
   docker logs quasar-relay-1
   ```

2. Verify NGINX configuration:
   ```bash
   docker exec relay-load-balancer nginx -t
   ```

3. Check if services are accessible:
   ```bash
   curl -k https://localhost
   ```

## 17. Automating SSL Certificate Renewal

```bash
crontab -e
```

Add the following line:
```
0 3 * * * certbot renew --quiet && cp /etc/letsencrypt/live/yourdomain.com/fullchain.pem /opt/quasar-relay/certs/ && cp /etc/letsencrypt/live/yourdomain.com/privkey.pem /opt/quasar-relay/certs/ && chmod 644 /opt/quasar-relay/certs/*.pem && docker restart relay-load-balancer
```

This will check for certificate renewal at 3 AM daily, and restart the NGINX container if renewed.

## 18. Setup Load Testing

For load testing, we'll set up a separate machine to generate traffic to your relay server. This will be covered in a separate guide.

## Next Steps

After successful deployment:
1. Monitor server performance during regular usage
2. Adjust rate limiting and security settings as needed
3. Set up load testing to verify scalability
4. Consider setting up a backup server in a different region for redundancy
