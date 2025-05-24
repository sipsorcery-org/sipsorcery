docker build -t sipsorcery/sipcallserver:0.5 .
docker push sipsorcery/sipcallserver:0.5

docker run -it --rm --name sip-callserver -p 5060:5060/udp -p 5061:5061/tcp sipsorcery/sipcallserver:0.2