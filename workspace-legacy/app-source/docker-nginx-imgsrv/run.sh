docker run \
    --rm \
    -it \
    --name nginx-image-server \
    -p 127.0.0.1:8082:8080 \
    -v /data1/userpics:/var/www/nginx/images \
    -e "SERVER_NAME=www.cichlids.com" \
    quay.io/wantedly/nginx-image-server:latest
