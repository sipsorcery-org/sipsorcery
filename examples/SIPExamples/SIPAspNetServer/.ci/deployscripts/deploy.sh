echo 'SIP demo server release deploy script starting...'
pwd
export

echo "Agent.BuildDirectory=$Agent_BuildDirectory"
echo "Build.Repository.LocalPath=$Build_Repository_LocalPath"
echo "Environment.ResourceName=$Environment_ResourceName"
echo "Environment.ResourceId=$Environment_ResourceId"
echo "System.DefaultWorkingDirectory=$System_DefaultWorkingDirectory"

sudo systemctl stop sipdemo
sudo cp -r  drop/* /opt/sipdemo
sudo systemctl start sipdemo
echo 'SIP demo server release deploy script finished.'
