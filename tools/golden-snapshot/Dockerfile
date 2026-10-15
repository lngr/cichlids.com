FROM postgres:18.6@sha256:74935e72241653ca55e0414067e6d8763aceb8a810eb51b452253ec3dcfc4336
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv \
    && rm -rf /var/lib/apt/lists/*
COPY requirements.txt /opt/golden/requirements.txt
RUN python3 -m venv /opt/venv && /opt/venv/bin/pip install --no-cache-dir -r /opt/golden/requirements.txt
COPY snapshot.py restore.py mask.sql schema.json /opt/golden/
ENV PATH="/opt/venv/bin:${PATH}" PYTHONDONTWRITEBYTECODE=1
USER 999:999
WORKDIR /opt/golden
ENTRYPOINT ["python", "/opt/golden/restore.py"]
