SHELL := /bin/bash

CERT_DIR ?= ./certs
.PHONY: all certs env build up logs down clean

all: up

certs:
	./scripts/generate-certs.sh $(CERT_DIR)

env:
	./scripts/generate-env.sh $(CERT_DIR) .env

build:
	docker compose build

up:
	docker compose up --build -d

logs:
	docker compose logs -f

down:
	docker compose down

clean:
	docker rm -f lovecraft-telegrambot lovecraft-webapi || true
	docker network rm lovecraft-net || true
