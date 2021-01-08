echo 'SIP demo server release deploy script starting...'
sudo systemctl stop sipdemo
sudo cp -r _sipsorcery.sipdemo/drop/* /opt/sipdemo
sudo systemctl start sipdemo
echo 'SIP demo server release deploy script finished.'
