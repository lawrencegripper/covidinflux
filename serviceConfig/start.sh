#!/bin/bash
set -e
cd "$(dirname "$0")"

# grafana-server web -config ./grafana.ini &
influxd --bolt-path ./influxConfig.db