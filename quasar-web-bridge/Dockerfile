FROM node:20-alpine

WORKDIR /app

# Copy package.json and package-lock.json files
COPY package*.json ./

# Install dependencies
RUN npm install --production

# Copy all other source code files
COPY . .

# Create logs and certs directories
RUN mkdir -p logs
RUN mkdir -p certs

# Expose the port the app runs on
EXPOSE 3000

# Command to run the application
CMD ["node", "server.js"]
