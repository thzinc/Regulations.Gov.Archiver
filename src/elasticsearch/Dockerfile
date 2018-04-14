FROM docker.elastic.co/elasticsearch/elasticsearch:6.2.3
RUN bin/elasticsearch-plugin install ingest-attachment
RUN bin/elasticsearch-plugin install https://github.com/spinscale/elasticsearch-ingest-opennlp/releases/download/6.2.3.1/ingest-opennlp-6.2.3.1.zip
COPY --chown=elasticsearch:elasticsearch elasticsearch.yml /usr/share/elasticsearch/config/