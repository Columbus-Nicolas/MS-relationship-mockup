FROM postgres:18-alpine
# Runs once, on the first start of an empty volume. The seed is run by hand.
COPY schema.sql /docker-entrypoint-initdb.d/
