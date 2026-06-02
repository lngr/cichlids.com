***************************
*       INSTALLATION      *
***************************

Be sure you have cURL installed.
Create a facebook app (https://developers.facebook.com/apps) with your domain and write down the App ID and the App Secret

Create a twitter app (https://dev.twitter.com/apps/new) with your domain and write down the Consumer ID and the Consumer Secret

1) Install extension
2) Insert a content element "login" on any page, can be on a page not visible to the user (e.g. "hide in menu"). 
   Write down uid of that record.
   That login should work (that means a user storage page with a user record and usergroup record has been created and the GRSP is configured)
3) Insert a content element "plugin" - "easylogin" on the login page
4) Include the static TypoScript, insert these constants into your TS template and fit them to your needs:

plugin.tx_dixeasylogin_pi1 {
		# if jQuery, jQueryUI and the lightness theme should be included by default. 
		# if turned off, you have to take care for yourself that the libraries are loaded (smart if other extensions also include jQuery)
	include_jQuery = 1

		# if the user should be created when not already found in the database
	allowCreate = 1

		# where the fe_users records should be stored when created
	user_pid = 6
	
		# when a user is created, he will get this usergroup(s)
	usergroup = 1
	
		# page where the "easylogin" plugin is located
		# used for the xrds definition
	pid_loginPage = 21
	
		# uid of the common login
	uid_felogin = 10
	
		# register a facebook app to get these two values
	facebook_appID = YOUR-APP-ID
	facebook_appSecret = YOUR-APP-SECRET
	
		# register a twitter app to get these two values
	twitter_consumerKey = YOUR-CONSUMER-KEY
	twitter_consumerSecret = YOUR-CONSUMER-SECRET
	
		# enable or disable login methods
	methods.felogin = 1
	methods.google = 1
	methods.yahoo = 1
	methods.myopenid = 1
	methods.wordpress = 1
	methods.facebook = 1
	methods.twitter = 1
}

See ext/dix_easylogin/ext_typoscript_setup.txt for further configuration