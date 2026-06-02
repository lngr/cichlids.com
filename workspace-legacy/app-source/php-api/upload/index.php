<?php


/**
 * Based on:
 * PHP Server-Side Example for Fine Uploader (traditional endpoint handler).
 * Maintained by Widen Enterprises.
 *
 * This example:
 *  - handles chunked and non-chunked requests
 *  - supports the concurrent chunking feature
 *  - assumes all upload requests are multipart encoded
 *  - handles delete requests
 *  - handles cross-origin environments
 *
 * 3. Ensure your php.ini file contains appropriate values for
 *    max_input_time, upload_max_filesize and post_max_size.
 *
 * 4. Ensure your "chunks" and "files" folders exist and are writable.
 *    "chunks" is only needed if you have enabled the chunking feature client-side.
 *
 * 5. If you have chunking enabled in Fine Uploader, you MUST set a value for the `chunking.success.endpoint` option.
 *    This will be called by Fine Uploader when all chunks for a file have been successfully uploaded, triggering the
 *    PHP server to combine all parts into one file. This is particularly useful for the concurrent chunking feature,
 *    but is now required in all cases if you are making use of this PHP example.
 */


require("../vendor/firebase/php-jwt/src/JWT.php");
require("../vendor/firebase/php-jwt/src/BeforeValidException.php");
require("../vendor/firebase/php-jwt/src/ExpiredException.php");
require("../vendor/firebase/php-jwt/src/SignatureInvalidException.php");

require("../src/Config.php");
require("../src/Users.php");
require("../src/Db.php");

// Include the upload handler class
require_once "handler.php";

$BASE_UPLOAD_DIR = "/var/www/uploads/";

$uploader = new UploadHandler();

// Specify the list of valid extensions, ex. array("jpeg", "xml", "bmp")
$uploader->allowedExtensions = array(
	// pictures
	'jpg', 'png', "jpeg", 
	// videos
	'mp4', 'mpg', "avi", "wmv", "flv", "webm", "mkv", "m4v", "mp2", "m2v", "mpeg", "3gp", "3g2",
);

// Specify max file size in bytes.
$uploader->sizeLimit = null;

// Specify the input name set in the javascript.
$uploader->inputName = "qqfile"; // matches Fine Uploader's default inputName value by default

// If you want to use the chunking/resume feature, specify the folder to temporarily save parts.
$uploader->chunksFolder = $BASE_UPLOAD_DIR . "chunks";

//$method = $_SERVER["REQUEST_METHOD"];
$method = get_request_method();

// This will retrieve the "intended" request method.  Normally, this is the
// actual method of the request.  Sometimes, though, the intended request method
// must be hidden in the parameters of the request.  For example, when attempting to
// send a DELETE request in a cross-origin environment in IE9 or older, it is not
// possible to send a DELETE request.  So, we send a POST with the intended method,
// DELETE, in a "_method" parameter.
function get_request_method() {
    global $HTTP_RAW_POST_DATA;

    // This should only evaluate to true if the Content-Type is undefined
    // or unrecognized, such as when XDomainRequest has been used to
    // send the request.
    if(isset($HTTP_RAW_POST_DATA)) {
    	parse_str($HTTP_RAW_POST_DATA, $_POST);
    }

    if (isset($_POST["_method"]) && $_POST["_method"] != null) {
        return $_POST["_method"];
    }

    return $_SERVER["REQUEST_METHOD"];
}


function parseRequestHeaders() {
    $headers = array();
    foreach($_SERVER as $key => $value) {
        if (substr($key, 0, 5) <> 'HTTP_') {
            continue;
        }
        $header = str_replace(' ', '-', ucwords(str_replace('_', ' ', strtolower(substr($key, 5)))));
        $headers[$header] = $value;
    }
    return $headers;
}

function handleCorsRequest() {
    header("Access-Control-Allow-Origin: *");
}

/*
 * handle pre-flighted requests. Needed for CORS operation
 */
function handlePreflight() {
    handleCorsRequest();
    header("Access-Control-Allow-Methods: POST");
    header("Access-Control-Allow-Credentials: true");
    header("Access-Control-Allow-Headers: Content-Type, X-Requested-With, Cache-Control, Authorization");
}

// Determine whether we are dealing with a regular ol' XMLHttpRequest, or
// an XDomainRequest
$_HEADERS = parseRequestHeaders();
$iframeRequest = false;
if (!isset($_HEADERS['X-Requested-With']) || $_HEADERS['X-Requested-With'] != "XMLHttpRequest") {
    $iframeRequest = true;
}

/*
 * handle the preflighted OPTIONS request. Needed for CORS operation.
 */
if ($method == "OPTIONS") {
    handlePreflight();
} else if ($method == "POST") {
    handleCorsRequest();
    header("Content-Type: text/plain");

    if (!isset($_HEADERS['Authorization']) 
	|| $_HEADERS['Authorization'] === ''
	|| substr($_HEADERS['Authorization'], 0, 7) <> 'Bearer '
	|| !preg_match("/Bearer\s+(.*)$/i", $_HEADERS['Authorization'], $matches)) {
    	return header("HTTP/1.0 401 Forbidden");
    }
    $token = $matches[1];

    $key = \Cichlids\Config::Auth0JWTPubkey();

    try {
	$jwt = \Firebase\JWT\JWT::decode($token, $key, ["RS256"]);
    } catch (Exception $exception) {
    	header("HTTP/1.0 401 Forbidden");
        print $exception->getMessage();
	return;
    }

    if (!$jwt) {
    	header("HTTP/1.0 500 Server error");
	return;
    }

    $userid = \Cichlids\Users::mapJwtSubjectToUserId($jwt);

    // Assumes you have a chunking.success.endpoint set to point here with a query parameter of "done".
    // For example: /myserver/handlers/endpoint.php?done
    if (isset($_GET["done"])) {
        $result = $uploader->combineChunks($BASE_UPLOAD_DIR . "files/$userid");
        $result["uploadName"] = $uploader->getUploadName();
        echo json_encode($result);
    }
    // Handles upload requests
    else {
        // Call handleUpload() with the name of the folder, relative to PHP's getcwd()
        $result = $uploader->handleUpload($BASE_UPLOAD_DIR . "files/$userid");

        // To return a name used for uploaded file you can use the following line.
        $result["uploadName"] = $uploader->getUploadName();

        // iframe uploads require the content-type to be 'text/html' and
        // return some JSON along with self-executing javascript (iframe.ss.response)
        // that will parse the JSON and pass it along to Fine Uploader via
        // window.postMessage
        if ($iframeRequest == true) {
            header("Content-Type: text/html");
            echo json_encode($result)."<script src='http://www.cichlids.com/api/upload/iframe.xss.response.js'></script>";
        } else {
            echo json_encode($result);
        }
    }
}
else {
    header("HTTP/1.0 405 Method Not Allowed");
}

?>
