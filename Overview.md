# Archiver

## Goals

* Preserve data
    * Store each document as-is in postgresql
    * Store attachments on Google Drive
    * Upload to Internet Archive as data is available
* Process data for analysis
    * Ingest into Elasticsearch
        * `unprocessed`
            * Unaltered DocumentStub documents
                * Include reference to source URL
                * Include reference to Internet Archive copy
                * Include retrieval date
            * Ingested files with minimal processing
                * Include reference to source URL
                * Include reference to Internet Archive copy
                * Include retrieval date
        * `processed` 
            * NER
            * Stemming
            * Fingerprinting
    * Use Kibana for inspection/visualization

## Logical parts

* Retriever
    * Regulations.Gov client
        * Asks Persister for control info; what point to resume retrieving data from
        * Sends Persister (string Key,IEnumerable<ExpectedPersistable>)
        * Sends Persister Persistable<DocumentStub>
        * Sends Persister Persistable<byte[]> for files
* Persister
    * Receives (string Key,IEnumerable<ExpectedPersistable>)
        * Writes record to PostgreSQL
    * Receives Persistable<T>
        * Updates records in PostgreSQL
        * Writes data to disk (Google Drive-mapped volume?)
    * Receives GetNextPersistables
        * Queries for Persistables that have not been archived
        * Sends sender IEnumerable<Persistable> for key from message
    * Receives ArchiveConfirmation
        * Updates records in PostgreSQL for key from message
    * GetNextIngestables
        * Send Ingester IEnumerable<Persistable> for key from message
* Archiver
    * Sends Persister GetNextPersistables
    * Receives IEnumerable<Persistable>
        * Upload to IAS3 with as much metadata as possible
    * Sends Persister ArchiveConfirmation
* Ingester
    * Sends Persister GetNextIngestables
    * Receives IEnumerable<Persistable>
        * Ingest Persistable into `unprocessed` index


## Resources

* [IAS3](https://github.com/vmbrasseur/IAS3API)