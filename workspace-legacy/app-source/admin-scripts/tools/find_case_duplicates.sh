#!/bin/sh

for a in * ; do
	COUNT=$( find . -iname "$a" | wc -l)
	if [ $COUNT -ge 2 ]; then
		php /home/alex/scripts/cichlids/tools/make_caseins.php "$a"
	fi
done
