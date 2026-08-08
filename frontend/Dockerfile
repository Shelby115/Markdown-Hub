FROM node:22-alpine AS build
WORKDIR /app
COPY package.json .
RUN npm install
COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf.template /etc/nginx/templates/default.conf.template

# Where this container proxies /api and /health. The nginx entrypoint renders the template with
# these at startup, so they must have defaults - otherwise the config renders as
# "proxy_pass http://:;" and nginx refuses to start.
#
# API_UPSTREAM_PORT is the API's port INSIDE the container network (the port it listens on),
# deliberately not named API_PORT: compose files commonly use API_PORT for the published host
# port, and feeding that value in here silently yields 502s on every API call.
ENV API_HOST=api
ENV API_UPSTREAM_PORT=8080

EXPOSE 80
