{{- define "expertise-api.name" -}}
{{- .Chart.Name }}
{{- end }}

{{- define "expertise-api.fullname" -}}
{{- printf "%s" .Release.Name }}
{{- end }}

{{- define "expertise-api.labels" -}}
app.kubernetes.io/name: {{ include "expertise-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end }}

{{- define "expertise-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "expertise-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}
