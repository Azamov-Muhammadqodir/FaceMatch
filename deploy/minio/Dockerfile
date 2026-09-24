# MinIO no longer publishes container images on Docker Hub, so the server is built from the official source.
# Override MINIO_VERSION with any commit/pseudo-version of github.com/minio/minio.
ARG GO_IMAGE=golang:1.24-alpine
ARG BASE_IMAGE=alpine:3.22

FROM ${GO_IMAGE} AS build
ARG MINIO_VERSION=v0.0.0-20260212201848-7aac2a2c5b7c
ENV CGO_ENABLED=0 GOTOOLCHAIN=auto
RUN go install -trimpath -ldflags "-s -w" github.com/minio/minio@${MINIO_VERSION}

FROM ${BASE_IMAGE}
RUN adduser -D -H -u 1000 minio && mkdir /data && chown minio /data
COPY --from=build /go/bin/minio /usr/local/bin/minio
USER minio
EXPOSE 9000 9001
VOLUME ["/data"]
ENTRYPOINT ["minio"]
CMD ["server", "/data", "--console-address", ":9001"]
