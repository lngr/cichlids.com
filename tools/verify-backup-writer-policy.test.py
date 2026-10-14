#!/usr/bin/env python3
"""Check writer policy probes with an offline AWS CLI substitute."""
from pathlib import Path
import os
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent


class WriterProbes(unittest.TestCase):
    def test_access_denied_is_required_and_configuration_payloads_are_replayed(self):
        with tempfile.TemporaryDirectory() as directory:
            temp = Path(directory)
            cli = temp / 'aws'
            cli.write_text('''#!/usr/bin/env python3
import os,sys,json
from pathlib import Path
args=sys.argv[1:]; op=args[1]
with open(os.environ['CALL_LOG'],'a') as log: log.write(op+' '+os.environ['AWS_ACCESS_KEY_ID']+'\\n')
if op.startswith('get-bucket-') or op=='get-object-lock-configuration':
 assert os.environ['AWS_ACCESS_KEY_ID']=='operator'
 print(json.dumps({'Policy':'{}'} if op=='get-bucket-policy' else {'Rules':[]} if op=='get-bucket-lifecycle-configuration' else {'ObjectLockConfiguration':{'ObjectLockEnabled':'Enabled'}}));sys.exit(0)
assert os.environ['AWS_ACCESS_KEY_ID']=='writer'
if op=='put-object':sys.exit(0)
if op=='get-object':Path(args[-5]).write_text('writer policy proof\\n');sys.exit(0)
if op.startswith('put-'):
 flag={'put-bucket-policy':'--policy','put-bucket-lifecycle-configuration':'--lifecycle-configuration','put-object-lock-configuration':'--object-lock-configuration'}[op]
 json.loads(Path(args[args.index(flag)+1].removeprefix('file://')).read_text())
mode=os.environ.get('PROBE_MODE','denied')
if mode=='allowed':sys.exit(0)
print('AccessDenied' if mode=='denied' else 'Could not connect to endpoint',file=sys.stderr);sys.exit(1)
''')
            cli.chmod(0o755)
            env_file = temp / 'env'
            env_file.write_text('CICHLIDS_S3_DB_WRITER_ACCESS_KEY="writer"\nCICHLIDS_S3_DB_WRITER_SECRET_KEY="dummy"\nCICHLIDS_S3_BACKUP_ACCESS_KEY="operator"\nCICHLIDS_S3_BACKUP_SECRET_KEY="dummy"\n')
            env = {**os.environ, 'PATH': str(temp)+':'+os.environ['PATH'],
                   'CICHLIDS_ENV': str(env_file), 'CALL_LOG': str(temp/'calls')}
            for mode, code in [('denied',0),('allowed',1),('network',1)]:
                result = subprocess.run(['bash', str(ROOT/'tools/verify-backup-writer-policy.sh')],
                                        env={**env,'PROBE_MODE':mode},capture_output=True,text=True)
                self.assertEqual(result.returncode,code,result.stdout+result.stderr)
            self.assertIn('put-object writer', (temp/'calls').read_text())


if __name__ == '__main__':
    unittest.main()
