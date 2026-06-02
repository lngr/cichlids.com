FROM    node:5.9
COPY    files/package.json /app/package.json
RUN     cd /app; npm install 
RUN     mkdir -p /app
COPY    files/dist /app/dist
COPY    files/run-app.sh /app/run-app.sh
EXPOSE  3001
CMD     [ "/bin/bash", "/app/run-app.sh" ]
