{{/*
Expand the name of the chart.
*/}}
{{- define "ua-edgetranslator.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a default fully qualified app name.
We truncate at 63 chars because some Kubernetes name fields are limited to this.
*/}}
{{- define "ua-edgetranslator.fullname" -}}
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
{{- define "ua-edgetranslator.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Common labels
*/}}
{{- define "ua-edgetranslator.labels" -}}
helm.sh/chart: {{ include "ua-edgetranslator.chart" . }}
{{ include "ua-edgetranslator.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels
*/}}
{{- define "ua-edgetranslator.selectorLabels" -}}
app.kubernetes.io/name: {{ include "ua-edgetranslator.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Name of the ServiceAccount to use.
*/}}
{{- define "ua-edgetranslator.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "ua-edgetranslator.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Fully qualified server image reference. image.tag defaults to .Chart.AppVersion.
*/}}
{{- define "ua-edgetranslator.image" -}}
{{- $tag := .Values.image.tag | default .Chart.AppVersion -}}
{{- printf "%s:%s" .Values.image.repository $tag -}}
{{- end }}

{{/*
Fully qualified driver-pack image reference. Tag defaults to .Chart.AppVersion.
*/}}
{{- define "ua-edgetranslator.drivers.image" -}}
{{- $tag := .Values.drivers.image.tag | default .Chart.AppVersion -}}
{{- printf "%s:%s" .Values.drivers.image.repository $tag -}}
{{- end }}

{{/*
Resolve the credentials Secret name.
existingSecret wins; otherwise the chart-managed name when create=true; else "".
*/}}
{{- define "ua-edgetranslator.secretName" -}}
{{- if .Values.credentials.existingSecret -}}
{{- .Values.credentials.existingSecret -}}
{{- else if .Values.credentials.create -}}
{{- printf "%s-credentials" (include "ua-edgetranslator.fullname" .) -}}
{{- end -}}
{{- end }}

{{/*
Ordered list of persistence volume keys (also their subpath under /app).
*/}}
{{- define "ua-edgetranslator.persistenceKeys" -}}
settings pki logs nodesets
{{- end }}

{{/*
Returns "true" when at least one enabled persistence volume uses hostPath.
*/}}
{{- define "ua-edgetranslator.hasHostPath" -}}
{{- $found := "" -}}
{{- range $key := splitList " " (include "ua-edgetranslator.persistenceKeys" .) -}}
{{- $vol := index $.Values.persistence $key -}}
{{- if and $vol.enabled (eq $vol.kind "hostPath") -}}
{{- $found = "true" -}}
{{- end -}}
{{- end -}}
{{- $found -}}
{{- end }}

{{/*
Returns "true" when the fix-hostpath-perms init container should be rendered.
fixHostPathPerms.enabled: true | false | auto (auto => only when a hostPath exists).
*/}}
{{- define "ua-edgetranslator.renderFixHostPath" -}}
{{- $mode := printf "%v" .Values.fixHostPathPerms.enabled -}}
{{- if eq $mode "true" -}}
true
{{- else if eq $mode "false" -}}
{{- else -}}
{{- include "ua-edgetranslator.hasHostPath" . -}}
{{- end -}}
{{- end }}

{{/*
Space-separated list of enabled persistence mount paths (for chown).
*/}}
{{- define "ua-edgetranslator.persistencePaths" -}}
{{- $paths := list -}}
{{- range $key := splitList " " (include "ua-edgetranslator.persistenceKeys" .) -}}
{{- $vol := index $.Values.persistence $key -}}
{{- if $vol.enabled -}}
{{- $paths = append $paths (printf "/app/%s" $key) -}}
{{- end -}}
{{- end -}}
{{- join " " $paths -}}
{{- end }}

{{/*
volumeMounts for the enabled persistence volumes (shared by init + server).
*/}}
{{- define "ua-edgetranslator.persistenceVolumeMounts" -}}
{{- $mounts := list -}}
{{- range $key := splitList " " (include "ua-edgetranslator.persistenceKeys" .) -}}
{{- $vol := index $.Values.persistence $key -}}
{{- if $vol.enabled -}}
{{- $mounts = append $mounts (dict "name" $key "mountPath" (printf "/app/%s" $key)) -}}
{{- end -}}
{{- end -}}
{{- toYaml $mounts -}}
{{- end }}
