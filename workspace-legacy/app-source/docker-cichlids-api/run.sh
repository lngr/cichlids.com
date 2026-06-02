docker run -it --detach \
	--name cichlids-api \
	-p 127.0.0.1:8081:80 \
	-v /var/www/html/www-cichlids/cichlids.extra/api:/var/www/html \
	-v /data1/userpics/api_uploads:/var/www/uploads \
	-v /data1/userpics/user_pics:/var/www/user_pics \
	-v /var/log/cichlids-api:/var/log/httpd cichlids-api

