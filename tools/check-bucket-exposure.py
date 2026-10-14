#!/usr/bin/env python3
"""Check bucket access configuration offline with explicit source allowlists."""
import re

WRITER_POLICY = '''
bucket = aws_s3_bucket.database_backup.id
policy = jsonencode({
  Version = "2012-10-17"
  Statement = [
    {
      Sid = "WriterBucketRead"
      Effect = "Allow"
      Principal = { AWS = "arn:aws:iam:::user/p${var.database_writer_primary_project_id}:${var.database_writer_access_key_id}" }
      Action = ["s3:ListBucket"]
      Resource = aws_s3_bucket.database_backup.arn
    },
    {
      Sid = "WriterObjects"
      Effect = "Allow"
      Principal = { AWS = "arn:aws:iam:::user/p${var.database_writer_primary_project_id}:${var.database_writer_access_key_id}" }
      Action = ["s3:GetObject", "s3:PutObject", "s3:AbortMultipartUpload"]
      Resource = "${aws_s3_bucket.database_backup.arn}/*"
    }
  ]
})
'''


def clean_source(text):
    pattern = r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*|\#[^\n]*'
    return re.sub(pattern, lambda match: match[0] if match[0].startswith('"') else '\n', text, flags=re.S)


def tokens(text):
    """Preserve strings while discarding whitespace and HCL comments."""
    pattern = r'"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\n]*|\#[^\n]*|[^\s]'
    return [t for t in re.findall(pattern, text, re.S)
            if not t.startswith(('/*', '//', '#'))]


def check(blocks, backup):
    failures = []
    for (kind, name), body in blocks.items():
        body = clean_source(body)
        label = f'{kind}.{name}'
        if kind in ('aws_s3_bucket', 'aws_s3_bucket_acl'):
            if re.search(r'\bdynamic\s+"', body):
                failures.append(f'{label}: computed bucket configuration is forbidden')
            acl = re.search(r'\bacl\s*=\s*([^\n]+)', body)
            if (kind == 'aws_s3_bucket_acl' and not acl) or (acl and ''.join(tokens(acl[1])) != '"private"'):
                failures.append(f'{label}: ACL must be explicitly private')
            if re.search(r'\b(access_control_policy|grant)\s*\{', body):
                failures.append(f'{label}: custom ACL grants are forbidden')
        if kind == 'aws_s3_bucket':
            for attribute in ('policy', 'website', 'cors_rule'):
                if re.search(r'\b' + attribute + r'\s*[={]', body):
                    failures.append(f'{label}: inline {attribute} is forbidden')
        if kind in ('aws_s3_bucket_website_configuration', 'aws_s3_bucket_cors_configuration'):
            failures.append(f'{label}: website and CORS configuration are forbidden')
        if kind == 'aws_s3_bucket_public_access_block':
            flags = ['block_public_acls', 'ignore_public_acls']
            if backup:
                flags += ['block_public_policy', 'restrict_public_buckets']
            for flag in flags:
                if not re.search(r'\b' + flag + r'\s*=\s*true[ \t]*(?:\n|$)', body):
                    failures.append(f'{label}: {flag} must be true')
        if kind == 'aws_s3_bucket_policy':
            if not backup or name != 'database_writer' or tokens(body) != tokens(WRITER_POLICY):
                failures.append(f'{label}: policy outside the exact database writer allowlist')
    return failures
