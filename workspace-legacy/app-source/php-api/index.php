<?php
require "vendor/autoload.php";
use Psr\Http\Message\RequestInterface as Request;
use Psr\Http\Message\ResponseInterface as Response;
use \Cichlids\Config as Config;
use \Cichlids\Util;

function startsWith($haystack, $needle)
{
     $length = strlen($needle);
     return (substr($haystack, 0, $length) === $needle);
}

$auth0pubkey = Config::Auth0JWTPubkey();

$app = new \Slim\App();

// middleware to retrieve IPs
$checkProxyHeaders = true;
$trustedProxies = [ '172.17.42.1' ];
$app->add(new RKA\Middleware\IpAddress($checkProxyHeaders, $trustedProxies));

$optionalAuth = array(
	array("GET", "/pictures/{slug}/comments"),
	array("GET", "/tanks/{tankid}/comments"),
	array("GET", "/comments"),
);

// JWT auth middleware
$container = $app->getContainer();
$container["jwt"] = function ($container) {
    return new StdClass;
};

$auth0jwtauth = new \Slim\Middleware\JwtAuthentication([
	"secure" => false,
	"attribute" => "jwt",
	"algorithm" => ["RS256"],
	"secret" => $auth0pubkey,
   	"callback" => function ($request, $response, $arguments) use ($container) {
        	$container["jwt"] = $arguments["decoded"];
    	},
]);

$auth0jwtauth->addRule(function($request) {
	global $optionalAuth;

	$route = $request->getAttribute('route');
	$pattern = $route->getPattern();
	$method = $request->getMethod();

	foreach($optionalAuth as $exclude) {
		//error_log("Testing $method / $pattern for optional auth: $exclude[0] / $exclude[1]");
		if ($exclude[0] === $method && $exclude[1] === $pattern)
		{
			$ha = $request->getHeaderLine("Authorization");
			return startsWith($ha, "Bearer");
		}
	}
	return true;
});

$app->options('/pictures', 'preflight');
$app->get('/pictures', '\Cichlids\Pictures:listPictures');

$app->options('/pictures/{slug}', 'preflight');
$app->get('/pictures/{slug}', '\Cichlids\Pictures:viewPicture');
$app->delete('/pictures/{slug}', function ($request, $response, $args) {
	return \Cichlids\Pictures::deletePicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);
$app->put('/pictures/{slug}', function ($request, $response, $args) {
	return \Cichlids\Pictures::editPutPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/pictures/{slug}/comments', 'preflight');

$app->get('/pictures/{slug}/comments', function ($request, $response, $args) {
	return \Cichlids\Pictures::listComments($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->post('/pictures/{slug}/comments', function ($request, $response, $args) {
	return \Cichlids\Pictures::postComment($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/pictures/{slug}/type', 'preflight');
$app->put('/pictures/{slug}/type', function ($request, $response, $args) {
	return \Cichlids\Pictures::changeType($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/pictures/{slug}/edit', 'preflight');
$app->get('/pictures/{slug}/edit', function ($request, $response, $args) {
	return \Cichlids\Pictures::editGetPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/pictures/{slug}/discard', 'preflight');
$app->post('/pictures/{slug}/discard', function ($request, $response, $args) {
	return \Cichlids\Pictures::discardPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/upload/files', 'preflight');
$app->get('/upload/files', function ($request, $response, $args) {
	return \Cichlids\Upload::listAvailFiles($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/upload/files/{fileid}/convertToPicture', 'preflight');
$app->post('/upload/files/{fileid}/convertToPicture', function ($request, $response, $args) {
	return \Cichlids\Upload::convertToPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/upload/files/{fileid}/convertToTankPicture', 'preflight');
$app->post('/upload/files/{fileid}/convertToTankPicture', function ($request, $response, $args) {
	return \Cichlids\Upload::convertToTankPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/upload/files/{fileid}/convertToProfileImage', 'preflight');
$app->post('/upload/files/{fileid}/convertToProfileImage', function ($request, $response, $args) {
	return \Cichlids\Upload::convertToProfileImage($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/upload/files/{fileid}/convertToAvatarImage', 'preflight');
$app->post('/upload/files/{fileid}/convertToAvatarImage', function ($request, $response, $args) {
	return \Cichlids\Upload::convertToAvatarImage($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks', 'preflight');
$app->get('/tanks', '\Cichlids\Tanks:listTanks');
$app->delete('/tanks/{tankid}', function ($request, $response, $args) {
	return \Cichlids\Tanks::deleteTank($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks/addFromPicture', 'preflight');
$app->post('/tanks/addFromPicture', function ($request, $response, $args) {
	return \Cichlids\Tanks::addTankFromPicture($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks/{tankid}', 'preflight');
$app->get('/tanks/{tankid}', '\Cichlids\Tanks:viewTank');
$app->put('/tanks/{tankid}', function ($request, $response, $args) {
	return \Cichlids\Tanks::editPutTank($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);
$app->options('/tanks/{tankid}/edit', 'preflight');
$app->get('/tanks/{tankid}/edit', function ($request, $response, $args) {
	return \Cichlids\Tanks::editGetTank($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks/{tankid}/images/{pictureid}', 'preflight');
$app->delete('/tanks/{tankid}/images/{pictureid}', function ($request, $response, $args) {
	return \Cichlids\Tanks::deletePictureFromTank($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks/{tankid}/images/{pictureid}/asMainImage', 'preflight');
$app->post('/tanks/{tankid}/images/{pictureid}/asMainImage', function ($request, $response, $args) {
	return \Cichlids\Tanks::setPictureAsMainImage($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/tanks/{tankid}/comments', 'preflight');
$app->get('/tanks/{tankid}/comments', '\Cichlids\Tanks:listComments');
$app->post('/tanks/{tankid}/comments', function ($request, $response, $args) {
	return \Cichlids\Tanks::postComment($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/comments/{id}', 'preflight');
$app->delete('/comments/{id}', function ($request, $response, $args) {
	return \Cichlids\Comments::deleteComment($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->options('/comments', 'preflight');
$app->get('/comments', '\Cichlids\Comments:listComments');

$app->get('/userid/{userid}', '\Cichlids\Users:viewUser');

$app->get('/auth0/login', '\Cichlids\Users:auth0Login');
$app->get('/auth0/getByEmail/{email}', '\Cichlids\Users:auth0getByEmail');

$app->options('/authuser', 'preflight');
$app->get('/authuser', function($request, $response, $args) {
	return \Cichlids\Users::getAuthUser($request, $response, $args, $this->jwt);
})->add($auth0jwtauth);

$app->run();

function preflight($request, $response, $args) {
	return \Cichlids\Util::addCors($response);
}
