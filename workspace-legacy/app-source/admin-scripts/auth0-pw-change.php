<?
  require_once("config.php");

  $conn = mysql_connect($cichlids_db_host, $cichlids_db_username, $cichlids_db_password);
  mysql_select_db($cichlids_db_db);


function get_token() {
	$curl = curl_init();

	curl_setopt_array($curl, array(
  		CURLOPT_URL => "https://cichlids.eu.auth0.com/oauth/token",
  		CURLOPT_RETURNTRANSFER => true,
  		CURLOPT_ENCODING => "",
  		CURLOPT_MAXREDIRS => 10,
  		CURLOPT_TIMEOUT => 30,
  		CURLOPT_HTTP_VERSION => CURL_HTTP_VERSION_1_1,
  		CURLOPT_CUSTOMREQUEST => "POST",
  		CURLOPT_POSTFIELDS => "{\"client_id\":\"REDACTED_AUTH0_CLIENT_ID\",\"client_secret\":\"REDACTED_AUTH0_CLIENT_SECRET\",\"audience\":\"https://cichlids.eu.auth0.com/api/v2/\",\"grant_type\":\"client_credentials\"}",
  		CURLOPT_HTTPHEADER => array(
    		"content-type: application/json"
  	),
	));

	$response = curl_exec($curl);
	$err = curl_error($curl);

	curl_close($curl);

	if ($err) {
  	echo "cURL Error #:" . $err;
	} else {
		return json_decode($response)->access_token;
	}
}

function api_request($token, $method, $url) {
	$curl = curl_init();
	curl_setopt_array($curl, array(
  		CURLOPT_URL => $url,
  		CURLOPT_RETURNTRANSFER => true,
  		CURLOPT_ENCODING => "",
  		CURLOPT_MAXREDIRS => 10,
  		CURLOPT_TIMEOUT => 30,
  		CURLOPT_HTTP_VERSION => CURL_HTTP_VERSION_1_1,
  		CURLOPT_CUSTOMREQUEST => $method,
  		CURLOPT_HTTPHEADER => array(
    			"authorization: Bearer $token"
  		),
	));

	$response = curl_exec($curl);
	$err = curl_error($curl);
	curl_close($curl);

	if ($err) {
  		echo "cURL Error #:" . $err;
		} else {
  	return json_decode($response);
	}
}

function api_request_body($token, $method, $url, $body) {
	$curl = curl_init();
	curl_setopt_array($curl, array(
  		CURLOPT_URL => $url,
  		CURLOPT_RETURNTRANSFER => true,
  		CURLOPT_ENCODING => "",
  		CURLOPT_MAXREDIRS => 10,
  		CURLOPT_TIMEOUT => 30,
  		CURLOPT_HTTP_VERSION => CURL_HTTP_VERSION_1_1,
  		CURLOPT_CUSTOMREQUEST => $method,
  		CURLOPT_POSTFIELDS => $body,
  		CURLOPT_HTTPHEADER => array(
    			"authorization: Bearer $token",
			"Content-Type: application/json",
  		),
	));

	$response = curl_exec($curl);
	$err = curl_error($curl);
	curl_close($curl);

	if ($err) {
  		echo "cURL Error #:" . $err;
		} else {
  	return json_decode($response);
	}
}


$token = get_token();

	$query = "SELECT * FROM fe_users 
		WHERE NOT uid in(23, 6304, 6550, 15430 , 23 , 6304 , 6550 , 15430 , 1898 , 15435 , 12170 )
		AND NOT password = ''
		ORDER BY uid ASC
		LIMIT 1
		";

	$res = mysql_query($query);
	$output = array();
	$count = 0;
	while($row = mysql_fetch_assoc($res)) {
		$useridres = api_request($token, "GET", "https://cichlids.eu.auth0.com/api/v2/users-by-email?fields=user_id&email=" . urlencode($row['email']));

		$userid = $useridres[0]->user_id;
		printf("%-5s\t%s\t->\t %s\n", $row['uid'], $userid);

		$data = array(
 			"name" => $row['name'],
			"connection" => "Username-Password-Authentication",
		);

		$apires = api_request_body($token, "PATCH", "https://cichlids.eu.auth0.com/api/v2/users/" . urlencode($userid), json_encode($data));
		if (isset($apires->statusCode)) {
			print_r($apires);
			if (!in_array($apires->statusCode, array(404, 400))) // 400 bei ungueltigem pw
				break;
		}

		usleep(1000 * 1000); // 600 ms
	}
?>
