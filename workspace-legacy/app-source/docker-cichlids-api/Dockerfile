FROM centos:6

RUN yum -y install epel-release
RUN rpm -Uvh https://mirror.webtatic.com/yum/el6/latest.rpm
RUn yum clean all
RUN yum -y update
RUN yum -y install php56w php56w-opcache php56w-mcrypt php56w-pdo php56w-mysql 
RUN yum install -y ImageMagick

EXPOSE 80

COPY sysconfig-httpd /etc/sysconfig/httpd
COPY httpd-config.conf /etc/httpd/conf.d/
RUN rm /etc/httpd/conf.d/welcome.conf

RUN mkdir -p /var/www/html

CMD ["/usr/sbin/apachectl", "start"]
