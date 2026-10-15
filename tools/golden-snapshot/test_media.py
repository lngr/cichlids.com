import hashlib
import unittest
from unittest.mock import Mock

import snapshot


class PublicVariants(unittest.TestCase):
    def test_public_variant_transfer_has_generated_metadata_only(self):
        client = Mock()
        image = b'\xff\xd8synthetic-photo\xff\xd9'
        client.download_fileobj.side_effect = lambda bucket, key, stream: stream.write(image)
        captured = {}

        def upload(stream, bucket, key, ExtraArgs):
            captured.update(body=stream.read(), bucket=bucket, key=key, options=ExtraArgs)

        client.upload_fileobj.side_effect = upload
        digest = snapshot.transfer_variant(client, 'synthetic-source', 'synthetic-golden',
                                           'variants/thumb/originals/SyntheticAlice.jpg.jpg',
                                           'golden/v1/media/variants/thumb/media-1.jpg')
        self.assertEqual(digest, hashlib.sha256(image).hexdigest())
        self.assertEqual(captured['body'], image)
        self.assertEqual(captured['options'], {'ContentType': 'image/jpeg', 'Metadata': {'sha256': digest}})
        self.assertEqual(captured['key'], 'golden/v1/media/variants/thumb/media-1.jpg')

    def test_originals_and_forum_attachments_are_never_requested(self):
        for key in ('originals/private.jpg', 'forum_attachments/guest.jpg', 'variants/copy/private.jpg'):
            client = Mock()
            with self.assertRaises(ValueError):
                snapshot.transfer_variant(client, 'source', 'golden', key, 'golden/v1/media/variants/thumb/media-1.jpg')
            client.download_fileobj.assert_not_called()


if __name__ == '__main__':
    unittest.main(verbosity=2)
