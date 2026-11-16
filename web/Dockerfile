FROM node:22-alpine AS build
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ .
RUN npx ng build

FROM nginx:1.27-alpine
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/web/browser /usr/share/nginx/html
EXPOSE 80
