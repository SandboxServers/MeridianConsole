{{/*
Expand the name of the chart.
*/}}
{{- define "meridian.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
*/}}
{{- define "meridian.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version as used by the chart label.
*/}}
{{- define "meridian.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "meridian.labels" -}}
helm.sh/chart: {{ include "meridian.chart" . }}
{{ include "meridian.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "meridian.selectorLabels" -}}
app.kubernetes.io/name: {{ include "meridian.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Service-specific fullname
Usage: {{ include "meridian.service.fullname" (dict "context" . "service" "gateway") }}
*/}}
{{- define "meridian.service.fullname" -}}
{{- $context := .context }}
{{- $service := .service }}
{{- printf "%s-%s" (include "meridian.fullname" $context) $service | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Service-specific labels
Usage: {{ include "meridian.service.labels" (dict "context" . "service" "gateway") }}
*/}}
{{- define "meridian.service.labels" -}}
{{- $context := .context }}
{{- $service := .service }}
{{ include "meridian.labels" $context }}
app.kubernetes.io/component: {{ $service }}
{{- end }}

{{/*
Service-specific selector labels
Usage: {{ include "meridian.service.selectorLabels" (dict "context" . "service" "gateway") }}
*/}}
{{- define "meridian.service.selectorLabels" -}}
{{- $context := .context }}
{{- $service := .service }}
{{ include "meridian.selectorLabels" $context }}
app.kubernetes.io/component: {{ $service }}
{{- end }}

{{/*
Create the name of the service account to use
*/}}
{{- define "meridian.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "meridian.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
PostgreSQL connection string builder
Usage: {{ include "meridian.postgresConnectionString" (dict "context" . "database" "dhadgar_identity") }}
*/}}
{{- define "meridian.postgresConnectionString" -}}
{{- $context := .context }}
{{- $database := .database }}
{{- $host := $context.Values.secrets.postgresHost | default (printf "%s-postgresql" $context.Release.Name) }}
{{- $port := $context.Values.secrets.postgresPort | default "5432" }}
{{- $user := $context.Values.secrets.postgresUser | default "dhadgar" }}
{{- printf "Host=%s;Port=%s;Database=%s;Username=%s;Password=$(POSTGRES_PASSWORD)" $host $port $database $user }}
{{- end }}

{{/*
RabbitMQ connection host
*/}}
{{- define "meridian.rabbitmqHost" -}}
{{- .Values.secrets.rabbitmqHost | default (printf "%s-rabbitmq" .Release.Name) }}
{{- end }}

{{/*
Redis connection string
*/}}
{{- define "meridian.redisConnectionString" -}}
{{- $host := .Values.secrets.redisHost | default (printf "%s-redis-master" .Release.Name) }}
{{- $port := .Values.secrets.redisPort | default "6379" }}
{{- printf "%s:%s,password=$(REDIS_PASSWORD)" $host $port }}
{{- end }}

{{/*
Image name builder
Usage: {{ include "meridian.image" (dict "context" . "service" "gateway") }}
*/}}
{{- define "meridian.image" -}}
{{- $context := .context }}
{{- $service := .service }}
{{- $serviceConfig := index $context.Values $service }}
{{- $registry := $context.Values.global.imageRegistry | default "" }}
{{- $repository := $serviceConfig.image.repository }}
{{- $tag := $serviceConfig.image.tag | default $context.Chart.AppVersion }}
{{- if $registry }}
{{- printf "%s/%s:%s" $registry $repository $tag }}
{{- else }}
{{- printf "%s:%s" $repository $tag }}
{{- end }}
{{- end }}

{{/*
Common environment variables for all services
*/}}
{{- define "meridian.commonEnv" -}}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.config.aspNetCoreEnvironment | quote }}
- name: ASPNETCORE_URLS
  value: "http://+:8080"
- name: RABBITMQ_HOST
  value: {{ include "meridian.rabbitmqHost" . | quote }}
- name: RABBITMQ_USERNAME
  valueFrom:
    secretKeyRef:
      name: {{ include "meridian.fullname" . }}-secrets
      key: rabbitmq-username
- name: RABBITMQ_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "meridian.fullname" . }}-secrets
      key: rabbitmq-password
- name: REDIS_CONNECTION_STRING
  value: {{ include "meridian.redisConnectionString" . | quote }}
- name: REDIS_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "meridian.fullname" . }}-secrets
      key: redis-password
{{- end }}

{{/*
PostgreSQL environment variable for database-backed services
Usage: {{ include "meridian.postgresEnv" (dict "context" . "database" "dhadgar_identity") }}
*/}}
{{- define "meridian.postgresEnv" -}}
{{- $context := .context }}
{{- $database := .database }}
- name: POSTGRES_PASSWORD
  valueFrom:
    secretKeyRef:
      name: {{ include "meridian.fullname" $context }}-secrets
      key: postgres-password
- name: ConnectionStrings__Postgres
  value: {{ include "meridian.postgresConnectionString" . | quote }}
{{- end }}

{{/*
Resource limits
Usage: {{ include "meridian.resources" (dict "context" . "service" "gateway") }}
*/}}
{{- define "meridian.resources" -}}
{{- $context := .context }}
{{- $service := .service }}
{{- $serviceConfig := index $context.Values $service }}
{{- if $serviceConfig.resources }}
{{- toYaml $serviceConfig.resources }}
{{- else }}
{{- toYaml $context.Values.common.resources }}
{{- end }}
{{- end }}
