#!/bin/sh
# Executado automaticamente pelo LocalStack assim que os serviços ficam prontos
# (ver /etc/localstack/init/ready.d no docker-compose.yml).
set -e

awslocal s3 mb s3://filesharing-dev --region us-east-1
awslocal s3api put-public-access-block \
  --bucket filesharing-dev \
  --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true

echo "Bucket 'filesharing-dev' criado e bloqueado para acesso público."
