#!/usr/bin/env bash
# Provisiona o ChocoLog no FIWARE direto na EC2, sem a aplicação (o botão "Provisionar tudo" faz o mesmo).
# Uso, dentro da EC2:  bash provisionar.sh
# Cria: service group (apikey chocolog), device choco001, limites (ChocoConfig) e a assinatura do STH-Comet.

set -u
HOST=${HOST:-localhost}
H=(-H "fiware-service: chocolog" -H "fiware-servicepath: /" -H "Content-Type: application/json")

step() { printf '\n%s\n' "$1"; }

step "1/4 Service group (apikey usada nos tópicos MQTT)"
curl -s -w ' -> HTTP %{http_code}\n' "${H[@]}" -X POST "http://$HOST:4041/iot/services" -d '{
  "services": [{ "apikey": "chocolog", "resource": "/iot/d", "entity_type": "ChocoLog", "autoprovision": false }]
}'

step "2/4 Device choco001 (t, h, l, a e o comando alert)"
curl -s -w ' -> HTTP %{http_code}\n' "${H[@]}" -X POST "http://$HOST:4041/iot/devices" -d '{
  "devices": [{
    "device_id": "choco001", "apikey": "chocolog",
    "entity_name": "urn:ngsi-ld:ChocoLog:001", "entity_type": "ChocoLog",
    "protocol": "PDI-IoTA-UltraLight", "transport": "MQTT",
    "commands": [{ "name": "alert", "type": "command" }],
    "attributes": [
      { "object_id": "t", "name": "temperature", "type": "Number" },
      { "object_id": "h", "name": "humidity", "type": "Number" },
      { "object_id": "l", "name": "luminosity", "type": "Number" },
      { "object_id": "a", "name": "alertState", "type": "Text" }
    ],
    "static_attributes": [
      { "name": "descricao", "type": "Text", "value": "ChocoLog" },
      { "name": "local", "type": "Text", "value": "Caminhao 1" }
    ]
  }]
}'

step "3/4 Limites (valores do chocolog.ino)"
curl -s -w ' -> HTTP %{http_code}\n' "${H[@]}" -X POST "http://$HOST:1026/v2/entities?options=upsert" -d '{
  "id": "urn:ngsi-ld:ChocoConfig:001", "type": "ChocoConfig",
  "tempMin": { "type": "Number", "value": 15 }, "tempMax": { "type": "Number", "value": 22 },
  "humMin": { "type": "Number", "value": 45 }, "humMax": { "type": "Number", "value": 60 },
  "lumMax": { "type": "Number", "value": 50 }, "window": { "type": "Number", "value": 12 }
}'

step "4/4 Assinatura: cada medida vai para o STH-Comet (formato NGSIv1 via httpCustom)"
curl -s -w ' -> HTTP %{http_code}\n' "${H[@]}" -X POST "http://$HOST:1026/v2/subscriptions" -d '{
  "description": "ChocoLog: historico no STH-Comet",
  "subject": {
    "entities": [{ "id": "urn:ngsi-ld:ChocoLog:001", "type": "ChocoLog" }],
    "condition": { "attrs": ["temperature", "humidity", "luminosity", "alertState"] }
  },
  "notification": {
    "httpCustom": {
      "url": "http://sth-comet:8666/notify",
      "json": {
        "contextResponses": [{
          "contextElement": {
            "id": "${id}", "type": "${type}", "isPattern": "false",
            "attributes": [
              { "name": "temperature", "type": "Number", "value": "${temperature}" },
              { "name": "humidity", "type": "Number", "value": "${humidity}" },
              { "name": "luminosity", "type": "Number", "value": "${luminosity}" },
              { "name": "alertState", "type": "Text", "value": "${alertState}" }
            ]
          },
          "statusCode": { "code": "200", "reasonPhrase": "OK" }
        }]
      }
    },
    "attrs": ["temperature", "humidity", "luminosity", "alertState"]
  },
  "throttling": 1
}'

printf '\nPronto. 201 = criado; 409 ou 422 = já existia (pode ignorar).\n'
