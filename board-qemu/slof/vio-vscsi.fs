\ *****************************************************************************
\ * Copyright (c) 2011 IBM Corporation
\ * All rights reserved.
\ * This program and the accompanying materials
\ * are made available under the terms of the BSD License
\ * which accompanies this distribution, and is available at
\ * http://www.opensource.org/licenses/bsd-license.php
\ *
\ * Contributors:
\ *     IBM Corporation - initial implementation
\ ****************************************************************************/

." Populating " pwd

false VALUE vscsi-debug?
0 VALUE vscsi-unit

\ -----------------------------------------------------------
\ Direct DMA conversion hack
\ -----------------------------------------------------------
: l2dma ( laddr - dma_addr)      
;

\ -----------------------------------------------------------
\ CRQ related functions
\ -----------------------------------------------------------

0    VALUE     crq-base
0    VALUE     crq-dma
0    VALUE     crq-offset
1000 CONSTANT  CRQ-SIZE

CREATE crq 10 allot

: crq-alloc ( -- )
    \ XXX We rely on SLOF alloc-mem being aligned
    CRQ-SIZE alloc-mem to crq-base 0 to crq-offset
    crq-base l2dma to crq-dma
;

: crq-free ( -- )
    vscsi-unit hv-free-crq
    crq-base CRQ-SIZE free-mem 0 to crq-base
;

: crq-init ( -- res )
    \ Allocate CRQ. XXX deal with fail
    crq-alloc

    vscsi-debug? IF
        ." VSCSI: allocated crq at " crq-base . cr
    THEN

    \ Clear buffer
    crq-base CRQ-SIZE erase

    \ Register with HV
    vscsi-unit crq-dma CRQ-SIZE hv-reg-crq

    \ Fail case
    dup 0 <> IF
        ." VSCSI: Error " . ."  registering CRQ !" cr
	crq-free
    THEN
;

: crq-cleanup ( -- )
    crq-base 0 = IF EXIT THEN

    vscsi-debug? IF
        ." VSCSI: freeing crq at " crq-base . cr
    THEN
    crq-free
;

: crq-send ( msgaddr -- true | false )
    vscsi-unit swap hv-send-crq 0 =
;

: crq-poll ( -- true | false)
    crq-offset crq-base + dup
    vscsi-debug? IF
        ." VSCSI: crq poll " dup .
    THEN
    c@
    vscsi-debug? IF
        ."  value=" dup . cr
    THEN
    80 and 0 <> IF
        dup crq 10 move
	0 swap c!
	crq-offset 10 + dup CRQ-SIZE >= IF drop 0 THEN to crq-offset
	true
    ELSE drop false THEN
;

: crq-wait ( -- true | false)
    \ FIXME: Add timeout
    0 BEGIN drop crq-poll dup not WHILE d# 1 ms REPEAT
    dup not IF
        ." VSCSI: Timeout waiting response !" cr EXIT
    ELSE
        vscsi-debug? IF
            ." VSCSI: got crq: " crq dup l@ . ."  " 4 + dup l@ . ."  "
	    4 + dup l@ . ."  " 4 + l@ . cr
        THEN
    THEN
;

\ -----------------------------------------------------------
\ CRQ encapsulated SRP definitions
\ -----------------------------------------------------------

01 CONSTANT VIOSRP_SRP_FORMAT
02 CONSTANT VIOSRP_MAD_FORMAT
03 CONSTANT VIOSRP_OS400_FORMAT
04 CONSTANT VIOSRP_AIX_FORMAT
06 CONSTANT VIOSRP_LINUX_FORMAT
07 CONSTANT VIOSRP_INLINE_FORMAT

struct
   1 field >crq-valid
   1 field >crq-format
   1 field >crq-reserved
   1 field >crq-status
   2 field >crq-timeout
   2 field >crq-iu-len
   8 field >crq-iu-data-ptr
constant /crq

: srp-send-crq ( addr len -- )
    80                crq >crq-valid c!
    VIOSRP_SRP_FORMAT crq >crq-format c!
    0                 crq >crq-reserved c!
    0                 crq >crq-status c!
    0                 crq >crq-timeout w!
    ( len )           crq >crq-iu-len w!
    ( addr ) l2dma    crq >crq-iu-data-ptr x!
    crq crq-send
    not IF
        ." VSCSI: Error sending CRQ !" cr
    THEN
;

: srp-wait-crq ( -- [tag true] | false )
    crq-wait not IF false EXIT THEN

    crq >crq-format c@ VIOSRP_SRP_FORMAT <> IF
    	." VSCSI: Unsupported SRP response: "
	crq >crq-format c@ . cr
	false EXIT
    THEN

    crq >crq-iu-data-ptr x@ true
;

\ Add scsi functions to dictionary
scsi-open


\ -----------------------------------------------------------
\ SRP definitions
\ -----------------------------------------------------------

0 VALUE >srp_opcode

00 CONSTANT SRP_LOGIN_REQ
01 CONSTANT SRP_TSK_MGMT
02 CONSTANT SRP_CMD
03 CONSTANT SRP_I_LOGOUT
c0 CONSTANT SRP_LOGIN_RSP
c1 CONSTANT SRP_RSP
c2 CONSTANT SRP_LOGIN_REJ
80 CONSTANT SRP_T_LOGOUT
81 CONSTANT SRP_CRED_REQ
82 CONSTANT SRP_AER_REQ
41 CONSTANT SRP_CRED_RSP
42 CONSTANT SRP_AER_RSP

02 CONSTANT SRP_BUF_FORMAT_DIRECT
04 CONSTANT SRP_BUF_FORMAT_INDIRECT

struct
   1 field >srp-login-opcode
   3 +
   8 field >srp-login-tag
   4 field >srp-login-req-it-iu-len
   4 +
   2 field >srp-login-req-buf-fmt
   1 field >srp-login-req-flags
   5 +
  10 field >srp-login-init-port-ids
  10 field >srp-login-trgt-port-ids
constant /srp-login

struct
   1 field >srp-lresp-opcode
   3 +
   4 field >srp-lresp-req-lim-delta
   8 field >srp-lresp-tag
   4 field >srp-lresp-max-it-iu-len
   4 field >srp-lresp-max-ti-iu-len
   2 field >srp-lresp-buf-fmt
   1 field >srp-lresp-flags
constant /srp-login-resp

struct
   1 field >srp-lrej-opcode
   3 +
   4 field >srp-lrej-reason
   8 field >srp-lrej-tag
   8 +
   2 field >srp-lrej-buf-fmt
constant /srp-login-rej

00 CONSTANT SRP_NO_DATA_DESC
01 CONSTANT SRP_DATA_DESC_DIRECT
02 CONSTANT SRP_DATA_DESC_INDIRECT

struct
    1 field >srp-cmd-opcode
    1 field >srp-cmd-sol-not
    3 +
    1 field >srp-cmd-buf-fmt
    1 field >srp-cmd-dout-desc-cnt
    1 field >srp-cmd-din-desc-cnt
    8 field >srp-cmd-tag
    4 +
    8 field >srp-cmd-lun
    1 +
    1 field >srp-cmd-task-attr
    1 +
    1 field >srp-cmd-add-cdb-len
   10 field >srp-cmd-cdb
    0 field >srp-cmd-cdb-add
constant /srp-cmd

struct
    1 field >srp-rsp-opcode
    1 field >srp-rsp-sol-not
    2 +
    4 field >srp-rsp-req-lim-delta
    8 field >srp-rsp-tag
    2 +
    1 field >srp-rsp-flags
    1 field >srp-rsp-status
    4 field >srp-rsp-dout-res-cnt
    4 field >srp-rsp-din-res-cnt
    4 field >srp-rsp-sense-len
    4 field >srp-rsp-resp-len
    0 field >srp-rsp-data
constant /srp-rsp

\ Constants for srp-rsp-flags
01 CONSTANT SRP_RSP_FLAG_RSPVALID
02 CONSTANT SRP_RSP_FLAG_SNSVALID
04 CONSTANT SRP_RSP_FLAG_DOOVER
05 CONSTANT SRP_RSP_FLAG_DOUNDER
06 CONSTANT SRP_RSP_FLAG_DIOVER
07 CONSTANT SRP_RSP_FLAG_DIUNDER

\ Storage for up to 256 bytes SRP request */
CREATE srp 100 allot
0 VALUE srp-len

: srp-prep-cmd-nodata ( srplun -- )
    srp /srp-cmd erase
    SRP_CMD srp >srp-cmd-opcode c!
    1 srp >srp-cmd-tag x!
    srp >srp-cmd-lun x!         \ 8 bytes lun
    /srp-cmd to srp-len   
;

: srp-prep-cmd-io ( addr len srplun -- )
    srp-prep-cmd-nodata		( addr len )
    swap l2dma			( len dmaaddr )
    srp srp-len +    		( len dmaaddr descaddr )
    dup >r x! r> 8 +		( len descaddr+8 )
    dup 0 swap l! 4 +		( len descaddr+c )
    l!    
    srp-len 10 + to srp-len
;

: srp-prep-cmd-read ( addr len srplun -- )
    srp-prep-cmd-io
    01 srp >srp-cmd-buf-fmt c!	\ in direct buffer
    1 srp >srp-cmd-din-desc-cnt c!
;

: srp-prep-cmd-write ( addr len srplun -- )
    srp-prep-cmd-io
    10 srp >srp-cmd-buf-fmt c!	\ out direct buffer
    1 srp >srp-cmd-dout-desc-cnt c!
;

: srp-send-cmd ( -- )
    vscsi-debug? IF
        ." VSCSI: Sending SCSI cmd " srp >srp-cmd-cdb c@ . cr
    THEN
    srp srp-len srp-send-crq
;

: srp-rsp-find-sense ( -- addr len true | false )
    srp >srp-rsp-flags c@ SRP_RSP_FLAG_SNSVALID and 0= IF
        false EXIT
    THEN
    \ XXX FIXME: We assume the sense data is right at response
    \            data. A different server might actually have both
    \            some response data we need to skip *and* some sense
    \            data.
    srp >srp-rsp-data srp >srp-rsp-sense-len l@ true
;

\ Wait for a response to the last sent SRP command
\ returns a SCSI status code or -1 (HW error).
\
: srp-wait-rsp ( -- stat )
    srp-wait-crq not IF false EXIT THEN
    dup 1 <> IF
        ." VSCSI: Invalid CRQ response tag, want 1 got " . cr
	-1 EXIT
    THEN drop
    
    srp >srp-rsp-tag x@ dup 1 <> IF
        ." VSCSI: Invalid SRP response tag, want 1 got " . cr
	-1 EXIT
    THEN drop
    
    srp >srp-rsp-status c@
    vscsi-debug? IF
        ." VSCSI: Got response status: "
	dup .status-text cr
    THEN
;

\ -----------------------------------------------------------
\ Perform SCSI commands
\ -----------------------------------------------------------

8000000000000000 INSTANCE VALUE current-target

\ SCSI command. We do *NOT* implement the "standard" execute-command
\ because that doesn't have a way to return the sense buffer back, and
\ we do have auto-sense with some hosts. Instead we implement a made-up
\ do-scsi-command.
\
\ Note: stat is -1 for "hw error" (ie, error queuing the command or
\ getting the response).
\
\ A sense buffer is returned whenever the status is non-0 however
\ if sense-len is 0 then no sense data is actually present
\
true  CONSTANT scsi-dir-read
false CONSTANT scsi-dir-write

: execute-scsi-command ( buf-addr buf-len dir cmd-addr cmd-len -- ... )
                       ( ... [ sense-buf sense-len ] stat )
    \ Stash command addr & len
    >r >r				( buf-addr buf-len dir )
    \ Command has no data ?
    over 0= IF
        3drop current-target srp-prep-cmd-nodata
    ELSE
        \ Command is a read ?
        current-target swap IF srp-prep-cmd-read ELSE srp-prep-cmd-write THEN
    THEN
    \ Recover command and copy it to our srp buffer
    r> r>
    srp >srp-cmd-cdb swap move
    srp-send-cmd
    srp-wait-rsp

    \ Check for HW error
    dup -1 = IF
        0 0 rot EXIT
    THEN

    \ Other error status
    dup 0<> IF
       srp-rsp-find-sense IF
           vscsi-debug? IF
               over scsi-get-sense-data
               ." VSCSI: Sense key [ " dup . ." ] " .sense-text
	       ."  ASC,ASCQ: " . . cr
           THEN
       ELSE 0 0
           \ This relies on auto-sense from qemu... if that isn't always the
           \ case we should request sense here
           ." VSCSI: No sense data" cr
       THEN
       rot
    THEN
;

\ Returns 1 for retry, 0 for return with no error and
\ -1 for return with an error
\
: check-retry-sense? ( sense-buf sense-len -- retry? )
    \ Check if the sense-len is at least 8 bytes
    8 < IF -1 EXIT THEN

    \ Fixed sense record, look for filemark etc...
    dup sense-data>response-code c@ 7e and 70 = IF
        dup sense-data>sense-key c@ e0 and IF drop -1 EXIT THEN
    THEN

    \ Get sense data
    scsi-get-sense-data? IF 	( ascq asc sense-key )
        \ No sense or recoverable, return success
	dup 2 < IF 3drop 0 EXIT THEN
	\ not ready and unit attention, retry
	dup 2 = swap 6 = or nip nip IF 1 EXIT THEN
    THEN
    \ Return failure
    -1
;

\ This is almost as the standard retry-command but returns
\ additionally the length of the returned sense information
\
\ The hw-err? field is gone, stat is -1 for a HW error, and
\ the sense data is provided iff stat is CHECK_CONDITION (02)
\
\ Additionally we wait 10ms between retries
\
0 INSTANCE VALUE rcmd-buf-addr
0 INSTANCE VALUE rcmd-buf-len
0 INSTANCE VALUE rcmd-dir
0 INSTANCE VALUE rcmd-cmd-addr
0 INSTANCE VALUE rcmd-cmd-len

: retry-scsi-command ( buf-addr buf-len dir cmd-addr cmd-len #retries -- ... )
                     ( ... 0 | [ sense-buf sense-len ] stat )
    >r \ stash #retries
    to rcmd-cmd-len to rcmd-cmd-addr to rcmd-dir to rcmd-buf-len to rcmd-buf-addr
    0 0 0  \ dummy status & semse
    r> \ retreive #retries              ( stat #retries )
    0 DO
        3drop  \ drop previous status & sense
	rcmd-buf-addr
	rcmd-buf-len
	rcmd-dir
	rcmd-cmd-addr
	rcmd-cmd-len
	execute-scsi-command		( [ sense-buf sense-len ] stat )

	\ Success ?
	dup 0= IF LEAVE THEN

	\ HW error ?
	dup -1 = IF LEAVE THEN

	\ Check condition ?
	dup 2 = IF  			( sense-buf sense-len stat )
	    >r	\ stash stat		( sense-buf sense len )
	    2dup
	    check-retry-sense?	        ( sense-buf sense-len retry? )
	    r> swap \ unstash stat	( sense-buf sense-len stat retry? )
	    \ Check retry? result
	    CASE
	         0 OF 3drop 0 LEAVE ENDOF	\ Swallow error, return 0
	        -1 OF LEAVE ENDOF		\ No retry
	    ENDCASE
        ELSE \ Anything other than busy -> exit
            dup 8 <> IF LEAVE THEN
	THEN
	a ms
    LOOP
;

\ -----------------------------------------------------------
\ Some command helpers
\ -----------------------------------------------------------

CREATE sector d# 512 allot
TRUE VALUE first-time-init?
0 VALUE open-count
CREATE cdb 10 allot
100 CONSTANT test-unit-retries

\ SCSI test-unit-read
: test-unit-ready ( -- 0 | [ sense-buf sense-len ] stat )
    vscsi-debug? IF
        ." VSCSI: test-unit-ready " current-target . cr
    THEN
    cdb scsi-build-test-unit-ready
    0 0 0 cdb scsi-param-size test-unit-retries retry-scsi-command
;

: inquiry ( -- buffer | NULL )
    vscsi-debug? IF
        ." VSCSI: inquiry " current-target . cr
    THEN
    \ WARNING: ATAPI devices with libata seem to ignore the MSB of
    \ the allocation length... let's only ask for ff bytes
    ff cdb scsi-build-inquiry
    \ 16 retries for inquiry to flush out any UAs
    sector ff scsi-dir-read cdb scsi-param-size 10 retry-scsi-command
    \ Success ?
    0= IF sector ELSE 2drop 0 THEN
;

: report-luns ( -- true | false )
    vscsi-debug? IF
        ." VSCSI: report luns " current-target . cr
    THEN
    200 cdb scsi-build-report-luns
    \ 16 retries to flush out any UAs
    sector 200 scsi-dir-read cdb scsi-param-size 10 retry-scsi-command
    \ Success ?
    0= IF true ELSE 2drop false THEN
;

: read-capacity ( -- true | false )
    vscsi-debug? IF
        ." VSCSI: read-capacity " current-target . cr
    THEN
    cdb scsi-build-read-cap-10
    sector scsi-length-read-cap-10-data scsi-dir-read
    cdb scsi-param-size 1 retry-scsi-command
    \ Success ?
    0= IF true ELSE 2drop false THEN
;

: start-stop-unit ( state# -- true | false )
    vscsi-debug? IF
        ." VSCSI: start-stop-unit " current-target . cr
    THEN
    cdb scsi-build-start-stop-unit
    0 0 0 cdb scsi-param-size 10 retry-scsi-command
    \ Success ?
    0= IF true ELSE 2drop false THEN
;

: get-media-event ( -- true | false )
    vscsi-debug? IF
        ." VSCSI: get-media-event " current-target . cr
    THEN
    cdb scsi-build-get-media-event
    sector scsi-length-media-event scsi-dir-read cdb scsi-param-size 1 retry-scsi-command
    \ Success ?
    0= IF true ELSE 2drop false THEN
;

: read-blocks ( -- addr block# #blocks blksz -- [ #read-blocks true ] | false )
    vscsi-debug? IF
        ." VSCSI: read-blocks " current-target . cr
    THEN
    over * 					( addr block# #blocks len )    
    >r rot r> 			                ( block# #blocks addr len )
    2swap                                       ( addr len block# #blocks )
    dup >r
    cdb scsi-build-read-10                      ( addr len )
    r> -rot                                     ( #blocks addr len )
    scsi-dir-read cdb scsi-param-size 10 retry-scsi-command
                                                ( #blocks [ sense-buf sense-len ] stat )
    0= IF true ELSE 3drop false THEN
;

\ Cleanup behind us
: vscsi-cleanup
    vscsi-debug? IF ." VSCSI: Cleaning up" cr THEN
    crq-cleanup

    \ Disable TCE bypass:
    vscsi-unit 0 rtas-set-tce-bypass
;

\ Initialize our vscsi instance
: vscsi-init ( -- true | false )
    vscsi-debug? IF ." VSCSI: Initializing" cr THEN

    my-unit to vscsi-unit

    \ Enable TCE bypass special qemu feature
    vscsi-unit 1 rtas-set-tce-bypass

    \ Initialize CRQ
    crq-init 0 <> IF false EXIT THEN

    \ Send init command
    " "(C0 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00)" drop
    crq-send not IF
        ." VSCSI: Error sending init command"
        crq-cleanup false EXIT
    THEN
 
    \ Wait reply
    crq-wait not IF
        crq-cleanup false EXIT
    THEN

    \ Check init reply
    crq c@ c0 <> crq 1 + c@ 02 <> or IF
        ." VSCSI: Initial handshake failed"
	crq-cleanup false EXIT
    THEN

    \ We should now login etc.. but we really don't need to
    \ with our qemu model

    \ Ensure we cleanup after booting
    first-time-init? IF
        ['] vscsi-cleanup add-quiesce-xt
	false to first-time-init?
    THEN

    true
;

: open
    vscsi-debug? IF ." VSCSI: Opening (count is " open-count . ." )" cr THEN

    open-count 0= IF
        vscsi-init IF
	    1 to open-count true
	ELSE ." VSCSI initialization failed !" cr false THEN
    ELSE
        open-count 1 + to open-count
        true
    THEN
;

: close
    vscsi-debug? IF ." VSCSI: Closing (count is " open-count . ." )" cr THEN

    open-count 0> IF
        open-count 1 - dup to open-count
	0= IF
	    vscsi-cleanup
	THEN
    THEN
;

\ -----------------------------------------------------------
\ SCSI scan at boot and child device support
\ -----------------------------------------------------------

\ We use SRP luns of the form 8000 | (bus << 8) | (id << 5) | lun
\ in the top 16 bits of the 64-bit LUN
: (set-target)
    to current-target
;
\ We obtain here a unit address on the stack, since our #address-cells
\ is 2, the 64-bit srplun is split in two cells that we need to join
\
\ Note: This diverges a bit from the original OF scsi spec as the two
\ cells are the 2 words of a 64-bit SRP LUN
: set-address ( srplun.lo srplun.hi -- )
    lxjoin (set-target)
;

\ We set max-transfer to a fixed value for now to avoid problems
\ with some CD-ROM drives.

: max-transfer ( -- n )
    10000 \ Larger value seem to have problems with some CDROMs
;

: dev-get-capacity ( -- blocksize #blocks )
    \ Make sure that there are zeros in the buffer in case something goes wrong:
    sector 10 erase
    \ Now issue the read-capacity command
    read-capacity not IF
        0 0 EXIT
    THEN
    sector scsi-get-capacity-10
;

: dev-read-blocks ( -- addr block# #blocks blksize -- #read-blocks )
    read-blocks    
;

: initial-test-unit-ready ( -- true | [ ascq asc sense-key false ] )
    test-unit-ready
    \ stat == 0, return
    0= IF true EXIT THEN
    \ check sense len, no sense -> return HW error
    0= IF drop 0 0 4 false EXIT THEN
    \ get sense
    scsi-get-sense-data false
;

: compare-sense ( ascq asc key ascq2 asc2 key2 -- true | false )
    3 pick =	    ( ascq asc key ascq2 asc2 keycmp )
    swap 4 pick =   ( ascq asc key ascq2 keycmp asccmp )
    rot 5 pick =    ( ascq asc key keycmp asccmp ascqcmp )
    and and nip nip nip
;

0 CONSTANT CDROM-READY
1 CONSTANT CDROM-NOT-READY
2 CONSTANT CDROM-NO-DISK
3 CONSTANT CDROM-TRAY-OPEN
4 CONSTANT CDROM-INIT-REQUIRED
5 CONSTANT CDROM-TRAY-MAYBE-OPEN

: cdrom-status ( -- status )
    initial-test-unit-ready
    IF CDROM-READY EXIT THEN

    vscsi-debug? IF
        ." TestUnitReady sense: " 3dup . . . cr
    THEN

    3dup 1 4 2 compare-sense IF
        3drop CDROM-NOT-READY EXIT
    THEN

    get-media-event IF
        sector w@ 4 >= IF
	    sector 2 + c@ 04 = IF
	        sector 5 + c@
		dup 02 and 0<> IF drop 3drop CDROM-READY EXIT THEN
		dup 01 and 0<> IF drop 3drop CDROM-TRAY-OPEN EXIT THEN
		drop 3drop CDROM-NO-DISK EXIT
	    THEN
	THEN
    THEN

    3dup 2 4 2 compare-sense IF
        3drop CDROM-INIT-REQUIRED EXIT
    THEN
    over 4 = over 2 = and IF
        \ Format in progress... what do we do ? Just ignore
	3drop CDROM-READY EXIT
    THEN
    over 3a = IF
        3drop CDROM-NO-DISK EXIT
    THEN

    \ Other error...
    3drop CDROM-TRAY-MAYBE-OPEN    
;

: cdrom-try-close-tray ( -- )
    scsi-const-load start-stop-unit drop
;

: cdrom-must-close-tray ( -- )
    scsi-const-load start-stop-unit not IF
        ." Tray open !" cr -65 throw
    THEN
;

: dev-prep-cdrom ( -- ready? )
    5 0 DO
        cdrom-status CASE
	    CDROM-READY           OF UNLOOP true EXIT ENDOF
	    CDROM-NO-DISK         OF ." No medium !" cr false EXIT ENDOF
	    CDROM-TRAY-OPEN       OF cdrom-must-close-tray ENDOF
	    CDROM-INIT-REQUIRED   OF cdrom-try-close-tray ENDOF
	    CDROM-TRAY-MAYBE-OPEN OF cdrom-try-close-tray ENDOF
	ENDCASE
	d# 1000 ms
    LOOP
    ." Drive not ready !" cr false
;

: dev-prep-disk ( -- ready? )
    initial-test-unit-ready not IF
        ." Disk not ready! Sense key :" . ."  ASC,ASCQ: " . . cr
        false EXIT
    THEN true
;

: vscsi-create-disk	( srplun -- )
    " disk" find-alias 0<> IF drop THEN
    get-node node>path
    20 allot
    " /disk@" string-cat                      \ srplun npath npathl
    rot base @ >r hex (u.) r> base ! string-cat
    " disk" 2swap set-alias
;

: vscsi-create-cdrom	( srplun -- )
    " cdrom" find-alias 0<> IF drop THEN
    get-node node>path
    20 allot
    " /disk@" string-cat                      \ srplun npath npathl
    rot base @ >r hex (u.) r> base ! string-cat
    " cdrom" 2swap set-alias
;

: wrapped-inquiry ( -- true | false )
    inquiry 0= IF false EXIT THEN
    \ Skip devices with PQ != 0
    sector inquiry-data>peripheral c@ e0 and 0 =
;

8 CONSTANT #dev

: vscsi-read-lun     ( addr -- lun true | false )
  dup c@ C0 AND CASE
     40 OF w@-be 3FFF AND TRUE ENDOF
     0  OF w@-be          TRUE ENDOF
     dup dup OF ." Unsupported LUN format = " . cr FALSE ENDOF
  ENDCASE
;

: vscsi-report-luns ( -- array ndev )
  \ array of pointers, up to 8 devices
  #dev 3 << alloc-mem dup
  0                                    ( devarray devcur ndev )   
  #dev 0 DO
     i 8 << 8000 or 30 << (set-target)
     report-luns IF
        sector l@                     ( devarray devcur ndev size )
        sector 8 + swap               ( devarray devcur ndev lunarray size )
        dup 8 + dup alloc-mem         ( devarray devcur ndev lunarray size size+ mem )
        dup rot 0 fill                ( devarray devcur ndev lunarray size mem )
        dup >r swap move r>           ( devarray devcur ndev mem )
        dup sector l@ 3 >> 0 DO       ( devarray devcur ndev mem memcur )
           dup dup vscsi-read-lun IF
              j 8 << 8000 or or 30 << swap x! 8 +
           ELSE
              2drop
           THEN
        LOOP drop
	rot                           ( devarray ndev mem devcur )
        dup >r x! r> 8 +              ( devarray ndev devcur )
        swap 1 +
     THEN
  LOOP
  nip
;

: vscsi-find-disks      ( -- )   
    ." VSCSI: Looking for devices" cr
    vscsi-report-luns
    0 ?DO
       dup x@
       BEGIN
          dup x@
          dup 0= IF drop TRUE ELSE
             (set-target) wrapped-inquiry IF	
	        ."   " current-target (u.) type ."  "
	        \ XXX FIXME: Check top bits to ignore unsupported units
	        \            and maybe provide better printout & more cases
                \ XXX FIXME: Actually check for LUNs
	        sector inquiry-data>peripheral c@ CASE
                   0   OF ." DISK     : " current-target vscsi-create-disk  ENDOF
                   5   OF ." CD-ROM   : " current-target vscsi-create-cdrom ENDOF
                   7   OF ." OPTICAL  : " current-target vscsi-create-cdrom ENDOF
                   e   OF ." RED-BLOCK: " current-target vscsi-create-disk  ENDOF
                   dup dup OF ." ? (" . 8 emit 29 emit 5 spaces ENDOF
                ENDCASE
	        sector .inquiry-text cr
	     THEN
             8 + FALSE
          THEN
       UNTIL drop
       8 +
    LOOP drop
;

\ Remove scsi functions from word list
scsi-close

: setup-alias
    " scsi" find-alias 0= IF
        " scsi" get-node node>path set-alias
    ELSE
        drop
    THEN 
;

: vscsi-init-and-scan  ( -- )
    \ Create instance for scanning:
    0 0 get-node open-node ?dup 0= IF EXIT THEN
    my-self >r
    dup to my-self
    \ Scan the VSCSI bus:
    vscsi-find-disks
    setup-alias
    \ Close the temporary instance:
    close-node
    r> to my-self
;

: vscsi-add-disk
    " scsi-disk.fs" included
;

vscsi-add-disk
vscsi-init-and-scan
