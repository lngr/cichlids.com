
mysqldump -u root -p \
    --databases cichlids_typo3 \
    --single-transaction \
    --compress \
    --order-by-primary  > cichlids_typo3.sql


#| mysql -u <RDS_user> \
#        --port=<port_number> \
#        --host=<host_name> \
##        -p<RDS_password>
