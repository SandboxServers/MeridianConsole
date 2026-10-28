# syntax=docker/dockerfile:1.7
# Lab-only RabbitMQ: the management image plus the delayed message exchange plugin (C10, D-ML36).
# The base minor and the plugin version must match. The plugin is fetched with a pinned sha256, so a changed release fails the build.
# Built by the lab stack as meridian-lab/rabbitmq:<sha8 of this file>; never used outside the lab.
FROM rabbitmq:4.1-management

ADD --chmod=0644 --checksum=sha256:567f876378e70af9d949de4066bbb2fc30162b46bcbc9efe2430076b172e4a87 \
    https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v4.1.0/rabbitmq_delayed_message_exchange-4.1.0.ez \
    /opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-4.1.0.ez

RUN rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange
