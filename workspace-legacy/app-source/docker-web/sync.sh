#(cd ../web && npm run build:prod)
rsync -Pav ../web/package.json files/
rsync -Pav ../web/dist files/
rsync -Pav . root@www.cichlids.com:docker-web
