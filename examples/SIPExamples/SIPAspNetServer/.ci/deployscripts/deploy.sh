echo 'SIP demo server release deploy script starting...'
pwd
export

echo "Agent.BuildDirectory=$Agent.BuildDirectory"
echo "Build.Repository.LocalPath=$Build.Repository.LocalPath"
echo "Environment.ResourceName=$Environment.ResourceName"
echo "Environment.ResourceId=$Environment.ResourceId"
echo "System.DefaultWorkingDirectory=$System.DefaultWorkingDirectory"

sudo systemctl stop sipdemo
sudo cp -r '_sipdemo publish/drop/*' /opt/sipdemo
sudo systemctl start sipdemo
echo 'SIP demo server release deploy script finished.'
