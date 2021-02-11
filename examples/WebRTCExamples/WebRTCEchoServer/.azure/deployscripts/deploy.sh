echo 'webrtcecho release deploy script starting...'
pwd

echo "AGENT_RELEASEDIRECTORY=$AGENT_RELEASEDIRECTORY"
echo "BUILD_DEFINITIONNAME=$BUILD_DEFINITIONNAME"
echo "RELEASE_PRIMARYARTIFACTSOURCEALIAS=$RELEASE_PRIMARYARTIFACTSOURCEALIAS"

sudo systemctl stop webrtcecho
sudo cp -r "$RELEASE_PRIMARYARTIFACTSOURCEALIAS"/drop/* /opt/webrtcecho
sudo systemctl start webrtcecho
echo 'webrtcecho release deploy script finished.'
