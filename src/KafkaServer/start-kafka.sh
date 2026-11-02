#!/usr/bin/env bash
set -eu

/etc/kafka/docker/run &
kafka_pid=$!

term() {
  kill "$kafka_pid" 2>/dev/null || true
  wait "$kafka_pid" 2>/dev/null || true
  exit 0
}
trap term TERM INT

for _ in $(seq 1 90); do
  if ! kill -0 "$kafka_pid" 2>/dev/null; then
    echo "Kafka process exited before it was ready" >&2
    wait "$kafka_pid" || true
    exit 1
  fi
  if /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list >/dev/null 2>&1; then
    /opt/kafka/bin/kafka-topics.sh \
      --bootstrap-server localhost:9092 \
      --create \
      --if-not-exists \
      --topic lightening-events \
      --partitions 1 \
      --replication-factor 1
    wait "$kafka_pid"
    exit $?
  fi
  sleep 2
done

echo "Kafka did not become ready in time" >&2
kill "$kafka_pid" 2>/dev/null || true
exit 1
