echo 'SIP demo server release deploy script starting...'
pwd

echo "AGENT_RELEASEDIRECTORY=$AGENT_RELEASEDIRECTORY"
echo "BUILD_DEFINITIONNAME=$BUILD_DEFINITIONNAME"
echo "RELEASE_PRIMARYARTIFACTSOURCEALIAS=$RELEASE_PRIMARYARTIFACTSOURCEALIAS"

sudo systemctl stop sipdemo
sudo cp -r "$RELEASE_PRIMARYARTIFACTSOURCEALIAS"/drop/* /opt/sipdemo
sudo systemctl start sipdemo
echo 'SIP demo server release deploy script finished.'
