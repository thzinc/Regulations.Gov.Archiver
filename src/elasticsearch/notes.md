# Elasticsearch scratch

```
DELETE my_index
PUT my_index
{
  "settings": {
    "analysis": {
      "analyzer": {
        "fingerprinter": {
          "tokenizer" : "uax_url_email",
          "filter" : ["lowercase", "asciifolding", "my_stemmer", "my_fingerprint"]
        }
      },
      "filter" : {
        "my_stemmer" : {
            "type" : "stemmer",
            "name" : "english"
        },
        "my_fingerprint" : {
            "type" : "fingerprint",
            "max_output_size" : "1024"
        }
      }
    }
  }
}

POST my_index/_analyze
{
  "analyzer": "fingerprinter",
  "text": "Dear Secretary of Health and Human Services Alex Azar,\n\nThe mission of the Department of Health and Human Services (HHS) is to put the health and well-being of the public first. But instead of working to ensure everyone has equal access to comprehensive and nondiscriminatory services, this proposed rule would expand the ability of institutions and entities, including hospitals, pharmacies, doctors, nurses, even receptionists, to use their religious or moral beliefs to discriminate and deny patients health care.\n\nPatients should not have to fear a provider will turn them away due to religious or moral beliefs and no doctor should be forced to withhold information or provide substandard care to a patient because of hospital executives' beliefs.\nYet, hospitals use religious beliefs to refuse to treat a woman seeking abortion, even when it is the standard of care; pharmacies cite religion to refuse to fill birth control prescriptions, and providers refuse to treat LGBTQ individuals simply because of their gender identity or sexual orientation. When these refusals happen, patients suffer harmful health consequences. The harms caused by refusals to provide care have a disproportionate impact on those who already face multiple barriers to care, including communities of color, LGBTQ individuals, people facing language barriers, and those struggling to make ends meet.\nHHS should not be looking for ways to embolden providers to discriminate against patients. Instead, HHS should follow its mission and commit to protect individuals' health and well-being and seek ways to address the real discrimination that women, LGBTQ people, and others face every day.\n\nSincerely,\nAndrea Hedgecock\n  Buffalo, NY 14213"
}

PUT _ingest/pipeline/opennlp-pipeline
{
  "description": "A pipeline to do named entity extraction",
  "processors": [
    {
      "split": {
        "field": "commentText",
        "separator": "\\n"
      }
    },
    {
      "foreach": {
        "field": "commentText",
        "processor": {
          "opennlp" : {
            "field" : "_ingest._value"
          }
        }
      }
    }
    
  ]
}

GET my_index/documentstub/HHS-OCR-2018-0002-21007

PUT my_index/documentstub/HHS-OCR-2018-0002-21007?pipeline=opennlp-pipeline
{
  "agencyAcronym": "HHS",
  "allowLateComment": false,
  "attachmentCount": 0,
  "commentDueDate": "2018-03-28T03:59:59+00:00",
  "commentStartDate": "2018-01-26T05:00:00+00:00",
  "commentText": "Dear Secretary of Health and Human Services Alex Azar,\n\nThe mission of the Department of Health and Human Services (HHS) is to put the health and well-being of the public first. But instead of working to ensure everyone has equal access to comprehensive and nondiscriminatory services, this proposed rule would expand the ability of institutions and entities, including hospitals, pharmacies, doctors, nurses, even receptionists, to use their religious or moral beliefs to discriminate and deny patients health care.\n\nPatients should not have to fear a provider will turn them away due to religious or moral beliefs and no doctor should be forced to withhold information or provide substandard care to a patient because of hospital executives' beliefs.\nYet, hospitals use religious beliefs to refuse to treat a woman seeking abortion, even when it is the standard of care; pharmacies cite religion to refuse to fill birth control prescriptions, and providers refuse to treat LGBTQ individuals simply because of their gender identity or sexual orientation. When these refusals happen, patients suffer harmful health consequences. The harms caused by refusals to provide care have a disproportionate impact on those who already face multiple barriers to care, including communities of color, LGBTQ individuals, people facing language barriers, and those struggling to make ends meet.\nHHS should not be looking for ways to embolden providers to discriminate against patients. Instead, HHS should follow its mission and commit to protect individuals' health and well-being and seek ways to address the real discrimination that women, LGBTQ people, and others face every day.\n\nSincerely,\nAndrea Hedgecock\n  Buffalo, NY 14213",
  "docketId": "HHS-OCR-2018-0002",
  "docketTitle": "Protecting Statutory Conscience Rights in Health Care; Delegations of Authority",
  "docketType": "Rulemaking",
  "documentId": "HHS-OCR-2018-0002-21007",
  "documentStatus": "Posted",
  "documentType": "Public Submission",
  "numberOfCommentsReceived": 1,
  "openForComment": false,
  "postedDate": "2018-03-29T04:00:00+00:00",
  "title": "Comment on FR Doc # 2018-01226"
}
```