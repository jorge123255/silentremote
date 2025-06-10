const express = require('express');
const http = require('http');
const https = require('https');
const WebSocket = require('ws');
const fs = require('fs');
const crypto = require('crypto');
const path = require('path');
const winston = require('winston');
const helmet = require('helmet');
const cors = require('cors');

// Initialize configuration
let config;
try {
  config = JSON.parse(fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8'));
  // Convert old format to new format if needed
  if (config.relayServer && !config.relayServers) {
    config.relayServers = [
      { 
        url: config.relayServer, 
        name: 'relay-1' 
      }
    ];
  }
} catch (err) {
  config = {
    port: process.env.PORT || 3000,
    relayServers: [
      { 
        url: process.env.RELAY_SERVER || 'ws://quasar-relay-1:8080',
        name: 'quasar-relay-1'
      },
      {
        url: 'ws://quasar-relay-2:8080',
        name: 'quasar-relay-2'
      }
    ],
    sessionExpiryMinutes: 30,
    logging: {
      level: 'info',
      directory: './logs'
    }
  };
}

// Create logs directory if it doesn't exist
if (!fs.existsSync(config.logging.directory)) {
  fs.mkdirSync(config.logging.directory, { recursive: true });
}

// Configure logger
const logger = winston.createLogger({
  level: config.logging.level || 'info',
  format: winston.format.combine(
    winston.format.timestamp(),
    winston.format.json()
  ),
  transports: [
    new winston.transports.File({ 
      filename: path.join(config.logging.directory, 'error.log'), 
      level: 'error' 
    }),
    new winston.transports.File({ 
      filename: path.join(config.logging.directory, 'combined.log') 
    }),
    new winston.transports.Console({
      format: winston.format.combine(
        winston.format.colorize(),
        winston.format.simple()
      )
    })
  ]
});

// Initialize Express
const app = express();

// Create HTTP or HTTPS server based on configuration
let server;

if (config.ssl && config.ssl.enabled) {
  try {
    const sslOptions = {
      key: fs.readFileSync(config.ssl.keyPath),
      cert: fs.readFileSync(config.ssl.certPath),
    };
    
    if (config.ssl.caPath) {
      sslOptions.ca = fs.readFileSync(config.ssl.caPath);
    }
    
    server = https.createServer(sslOptions, app);
    logger.info('HTTPS server created with SSL certificates');
  } catch (err) {
    logger.error(`Failed to load SSL certificates: ${err.message}`);
    logger.warn('Falling back to HTTP (insecure)');
    server = http.createServer(app);
  }
} else {
  logger.info('SSL not configured, using HTTP (insecure)');
  server = http.createServer(app);
}

// Configure middleware
app.use(helmet());
app.use(cors());
app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(express.static(path.join(__dirname, 'public')));

// Session storage
const webSessions = new Map();
// Tracking relay server usage for load balancing
let lastRelayServerIndex = 0;

// Function to select a relay server using round-robin
function selectRelayServer() {
  if (!config.relayServers || config.relayServers.length === 0) {
    logger.warn('No relay servers configured!');
    return { url: 'ws://localhost:8080', name: 'default' };
  }
  
  lastRelayServerIndex = (lastRelayServerIndex + 1) % config.relayServers.length;
  return config.relayServers[lastRelayServerIndex];
}

// Create session endpoint
app.post('/session/create', (req, res) => {
  const { serverId, sessionName, expiresInMinutes = 30, oneTimeSession = true } = req.body;
  
  if (!serverId) {
    return res.status(400).json({ error: 'Missing serverId parameter' });
  }
  
  // Generate session key
  const sessionKey = crypto.randomBytes(16).toString('hex');
  const expiresAt = Date.now() + (expiresInMinutes * 60 * 1000);
  
  // Select a relay server for this session
  const relayServer = selectRelayServer();
  
  // Store session
  webSessions.set(sessionKey, {
    sessionKey,
    sessionName: sessionName || 'Web Support Session',
    serverId,
    createdAt: Date.now(),
    expiresAt,
    oneTimeSession,
    used: false,
    relayServer // Store which relay server this session will use
  });
  
  logger.info(`Web session created: ${sessionKey} for server ${serverId} using relay ${relayServer.name}`);
  
  // Generate session URL
  const host = req.headers.host || 'localhost:3000';
  const protocol = req.secure ? 'https' : 'http';
  const sessionUrl = `${protocol}://${host}/client/connect?sessionKey=${sessionKey}`;
  
  res.json({
    sessionKey,
    sessionName: sessionName || 'Web Support Session',
    serverId,
    expiresAt,
    sessionUrl,
    relayServer: relayServer.url,
    relayServerName: relayServer.name
  });
});

// Client connection page
app.get('/client/connect', (req, res) => {
  const { sessionKey } = req.query;
  
  if (!sessionKey || !webSessions.has(sessionKey)) {
    return res.status(404).send('Invalid or expired session');
  }
  
  const session = webSessions.get(sessionKey);
  
  // Check if expired
  if (Date.now() > session.expiresAt) {
    webSessions.delete(sessionKey);
    return res.status(401).send('Session expired');
  }
  
  res.sendFile(path.join(__dirname, 'public', 'client.html'));
});

// Session validation API
app.get('/api/session/:sessionKey', (req, res) => {
  const { sessionKey } = req.params;
  
  if (!sessionKey || !webSessions.has(sessionKey)) {
    return res.status(404).json({ valid: false, error: 'Invalid session key' });
  }
  
  const session = webSessions.get(sessionKey);
  
  // Check if expired
  if (Date.now() > session.expiresAt) {
    webSessions.delete(sessionKey);
    return res.status(401).json({ valid: false, error: 'Session expired' });
  }
  
  // Check if already used (for one-time sessions)
  if (session.oneTimeSession && session.used) {
    return res.status(401).json({ valid: false, error: 'Session already used' });
  }
  
  // Mark as used if it's a one-time session
  if (session.oneTimeSession) {
    session.used = true;
  }
  
  // Get the relay server assigned to this session or select a new one
  const relayServer = session.relayServer || selectRelayServer();
  
  res.json({
    valid: true,
    sessionKey,
    sessionName: session.sessionName,
    serverId: session.serverId,
    relayServer: relayServer.url,
    relayServerName: relayServer.name
  });
});

// Health check endpoint
app.get('/health', (req, res) => {
  res.status(200).send('healthy');
});

// Start server
server.listen(config.port, () => {
  logger.info(`Web bridge server started on port ${config.port}`);
  if (config.relayServers && config.relayServers.length > 0) {
    logger.info(`Using ${config.relayServers.length} relay servers:`);
    config.relayServers.forEach(relay => {
      logger.info(`- ${relay.name}: ${relay.url}`);
    });
  } else {
    logger.warn('No relay servers configured!');
  }
});

// Clean up expired sessions
setInterval(() => {
  const now = Date.now();
  let expiredCount = 0;
  
  webSessions.forEach((session, key) => {
    if (now > session.expiresAt) {
      webSessions.delete(key);
      expiredCount++;
    }
  });
  
  if (expiredCount > 0) {
    logger.info(`Cleaned up ${expiredCount} expired sessions`);
  }
}, 60000); // Check every minute