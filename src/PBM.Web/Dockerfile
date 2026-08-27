FROM node:22-alpine AS build
WORKDIR /app
COPY src/PBM.Web/package*.json ./
RUN npm install
COPY src/PBM.Web/ ./
RUN npm run build

FROM nginx:1.27-alpine
COPY src/PBM.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
