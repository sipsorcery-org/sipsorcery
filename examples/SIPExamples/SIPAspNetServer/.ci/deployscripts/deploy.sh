echo 'SIP demo server release deploy script starting...'
pwd
sudo cp -r $(System.DefaultWorkingDirectory)/_sipsorcery.sipdemo/drop/* /opt/sipdemo
echo 'SIP demo server release deploy script finished.'
