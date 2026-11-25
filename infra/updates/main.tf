terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.0" }
  }
}

variable "aws_region" {
  type    = string
  default = "us-east-1"
}
variable "release_bucket_name" {
  type        = string
  description = "Globally unique private bucket name for Coral Windows releases."
  default     = "updates-coral-restaurant-site"
}
variable "domain_name" {
  type    = string
  default = "updates.coral.restaurant"
}
variable "root_domain" {
  type    = string
  default = "coral.restaurant"
}

provider "aws" { region = var.aws_region }

resource "aws_s3_bucket" "releases" { bucket = var.release_bucket_name }

resource "aws_s3_bucket_public_access_block" "releases" {
  bucket                  = aws_s3_bucket.releases.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_ownership_controls" "releases" {
  bucket = aws_s3_bucket.releases.id
  rule { object_ownership = "BucketOwnerEnforced" }
}

resource "aws_cloudfront_origin_access_control" "releases" {
  name                              = "coral-windows-releases"
  description                       = "CloudFront access to private Coral Windows releases"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

data "aws_cloudfront_cache_policy" "optimized" { name = "Managed-CachingOptimized" }

data "aws_route53_zone" "main" { name = var.root_domain }

resource "aws_acm_certificate" "releases" {
  domain_name       = var.domain_name
  validation_method = "DNS"
  lifecycle { create_before_destroy = true }
}

resource "aws_route53_record" "certificate_validation" {
  for_each = {
    for dvo in aws_acm_certificate.releases.domain_validation_options : dvo.domain_name => {
      name   = dvo.resource_record_name
      record = dvo.resource_record_value
      type   = dvo.resource_record_type
    }
  }
  allow_overwrite = true
  zone_id         = data.aws_route53_zone.main.zone_id
  name            = each.value.name
  records         = [each.value.record]
  ttl             = 60
  type            = each.value.type
}

resource "aws_acm_certificate_validation" "releases" {
  certificate_arn         = aws_acm_certificate.releases.arn
  validation_record_fqdns = [for record in aws_route53_record.certificate_validation : record.fqdn]
}

resource "aws_cloudfront_distribution" "releases" {
  enabled             = true
  is_ipv6_enabled     = true
  comment             = "Coral Windows update releases"
  default_root_object = "windows/stable.json"
  aliases             = [var.domain_name]

  origin {
    domain_name              = aws_s3_bucket.releases.bucket_regional_domain_name
    origin_id                = "coral-windows-releases"
    origin_access_control_id = aws_cloudfront_origin_access_control.releases.id
  }

  default_cache_behavior {
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "coral-windows-releases"
    viewer_protocol_policy = "https-only"
    cache_policy_id        = data.aws_cloudfront_cache_policy.optimized.id
  }

  ordered_cache_behavior {
    path_pattern           = "/windows/stable.json"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "coral-windows-releases"
    viewer_protocol_policy = "https-only"
    min_ttl                = 0
    default_ttl            = 0
    max_ttl                = 60
    forwarded_values {
      query_string = false
      cookies { forward = "none" }
    }
  }

  restrictions {
    geo_restriction { restriction_type = "none" }
  }
  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate_validation.releases.certificate_arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }
}

resource "aws_route53_record" "releases" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = var.domain_name
  type    = "A"
  alias {
    name                   = aws_cloudfront_distribution.releases.domain_name
    zone_id                = aws_cloudfront_distribution.releases.hosted_zone_id
    evaluate_target_health = false
  }
}

data "aws_iam_policy_document" "releases" {
  statement {
    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.releases.arn}/*"]
    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.releases.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "releases" {
  bucket = aws_s3_bucket.releases.id
  policy = data.aws_iam_policy_document.releases.json
}

output "update_manifest_url" { value = "https://${var.domain_name}/windows/stable.json" }
output "release_bucket_name" { value = aws_s3_bucket.releases.bucket }
output "cloudfront_distribution_id" { value = aws_cloudfront_distribution.releases.id }
