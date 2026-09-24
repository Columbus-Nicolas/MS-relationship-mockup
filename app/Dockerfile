FROM node:24-alpine
WORKDIR /app
# pg_dump / pg_restore for the Backups page - the same major version as the database.
RUN apk add --no-cache postgresql18-client
COPY package.json package-lock.json ./
RUN npm ci --omit=dev
COPY server.js index.html ./
COPY migrations ./migrations
USER node
EXPOSE 8080
CMD ["node", "server.js"]
